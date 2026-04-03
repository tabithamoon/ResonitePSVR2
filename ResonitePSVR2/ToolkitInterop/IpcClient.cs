using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ResonitePSVR2.ToolkitInterop;

// This is where we grab eye tracking data from PSVR2Toolkit
public class IpcClient {
	private const ushort IPC_SERVER_PORT = 3364;
	private const ushort k_unIpcVersion = 1;

	private static IpcClient m_pInstance;

	private bool m_running = false;
	private TcpClient? m_client;
	private NetworkStream? m_stream;
	private Thread? m_receiveThread;
	
	private readonly object m_gazeStateLock = new object();
	private TaskCompletionSource<IpcProtocol.CommandDataServerGazeDataResult>? m_gazeTask;
	private CancellationTokenSource m_forceShutdownToken;
	private int m_gazePumpPeriodMs = 8; // 120Hz
	private IpcProtocol.CommandDataServerGazeDataResult? m_lastGazeState = null;
	
	public static IpcClient Instance() {
		if ( m_pInstance == null ) {
			m_pInstance = new IpcClient();
		}
		return m_pInstance;
	}
	/*public void SetLogger(ILogger logger) {
		m_logger = logger;
	}*/
	
	public bool Start() {
		if ( m_running ) {
			return false;
		}

		try {
			m_client = new TcpClient();
			m_client.Connect("127.0.0.1", IPC_SERVER_PORT);
			if ( m_client.Connected ) {
				m_stream = m_client.GetStream();

				m_running = true;
				m_forceShutdownToken = new CancellationTokenSource();
				m_receiveThread = new Thread(() => ReceiveLoop(m_forceShutdownToken.Token));
				m_receiveThread.Start();
				return true;
			}
			return false;
		} catch ( SocketException ex ) {
			ResonitePSVR2.Msg($"[IPC_CLIENT] Connection failed. LastError = {ex.SocketErrorCode}");
			return false;
		}
	}
	
	public void Stop() {
		if ( !m_running ) {
			return;
		}

		m_running = false;
		m_forceShutdownToken.Cancel();

		lock ( m_gazeStateLock ) {
			m_gazeTask?.TrySetCanceled();
			m_gazeTask = null;
		}

		try {
			m_stream?.Close();
			m_client?.Close();
		} catch { }

		if ( m_receiveThread != null && m_receiveThread.IsAlive ) {
			if ( !m_receiveThread.Join(2000) ) {
				m_receiveThread.Interrupt();
			}
		}

		m_stream?.Dispose();
		m_client?.Dispose();
		m_forceShutdownToken.Dispose();
	}
	
	private void ReceiveLoop(CancellationToken token) {
        byte[] buffer = new byte[1024];

        try {
            var clientSocket = m_client!.Client;
            m_stream!.ReadTimeout = 1; // make the underlying stream non-blocking

            IpcProtocol.CommandDataClientRequestHandshake clientHandshakeRequest = new IpcProtocol.CommandDataClientRequestHandshake() {
                ipcVersion = k_unIpcVersion,
                processId = ( uint ) Process.GetCurrentProcess().Id
            };
            SendIpcCommand(IpcProtocol.ECommandType.ClientRequestHandshake, clientHandshakeRequest);

            var sw = Stopwatch.StartNew();
            long nextPumpMs = sw.ElapsedMilliseconds;

            while ( m_running && !token.IsCancellationRequested ) {
                // query gaze state every so often
                var now = sw.ElapsedMilliseconds;
                if ( now >= nextPumpMs ) {
                    SendIpcCommand(IpcProtocol.ECommandType.ClientRequestGazeData);
                    nextPumpMs = now + m_gazePumpPeriodMs;
                }

                bool readable = clientSocket.Poll(1000 /* 1ms */, SelectMode.SelectRead);
                if ( readable && clientSocket.Available > 0 ) {
                    int available = clientSocket.Available;
                    if ( available > buffer.Length ) {
                        buffer = new byte[Math.Max(available, buffer.Length * 2)];
                    }

                    int bytesRead = m_stream.Read(buffer, 0, Math.Min(buffer.Length, available));
                    if ( bytesRead <= 0 ) {
                        ResonitePSVR2.Msg("[IPC_CLIENT] Disconnected from server.");
                        break;
                    }

                    if ( bytesRead < Marshal.SizeOf<IpcProtocol.CommandHeader>() ) {
                        ResonitePSVR2.Msg("[IPC_CLIENT] Received invalid command header size.");
                        continue;
                    }

                    HandleIpcCommand(buffer, bytesRead);
                    continue;
                }

                Thread.Sleep(1);
            }
        } catch ( OperationCanceledException ) {
            // nothing special, this is from shutdown most likely
        } catch ( Exception ex ) {
            if ( m_running ) {
                ResonitePSVR2.Msg($"[IPC_CLIENT] Error in receive loop: {ex.Message}");
            }
        }
	}
	
	private void HandleIpcCommand(byte[] pBuffer, int bytesReceived) {
        IpcProtocol.CommandHeader header = ByteArrayToStructure<IpcProtocol.CommandHeader>(pBuffer, 0);

        switch ( header.type ) {
            case IpcProtocol.ECommandType.ServerPong: {
                ResonitePSVR2.Msg("[IPC_CLIENT] Received Pong from server.");
                    break;
                }

            case IpcProtocol.ECommandType.ServerHandshakeResult: {
                    if ( header.dataLen == Marshal.SizeOf<IpcProtocol.CommandDataServerHandshakeResult>() ) {
                        IpcProtocol.CommandDataServerHandshakeResult response = ByteArrayToStructure<IpcProtocol.CommandDataServerHandshakeResult>(pBuffer, Marshal.SizeOf<IpcProtocol.CommandHeader>());
                        switch ( response.result ) {
                            case IpcProtocol.EHandshakeResult.Success: {
                                ResonitePSVR2.Msg("[IPC_CLIENT] Handshake successful!");
                                break;
                            }
                            case IpcProtocol.EHandshakeResult.Failed: {
                                ResonitePSVR2.Msg("[IPC_CLIENT] Handshake failed!");
                                break;
                            }
                            case IpcProtocol.EHandshakeResult.Outdated: {
                                ResonitePSVR2.Msg($"[IPC_CLIENT] Handshake failed with reason: Outdated client. Please upgrade to an IPC version of {response.ipcVersion}");
                                break;
                            }
                        }
                    }
                    break;
                }
            case IpcProtocol.ECommandType.ServerGazeDataResult: {
                if ( header.dataLen == Marshal.SizeOf<IpcProtocol.CommandDataServerGazeDataResult>() ) {
                    IpcProtocol.CommandDataServerGazeDataResult response = ByteArrayToStructure<IpcProtocol.CommandDataServerGazeDataResult>(pBuffer, Marshal.SizeOf<IpcProtocol.CommandHeader>());
                    m_lastGazeState = response;

                }
                break;
            }
        }
    }

    private void SendIpcCommand<T>(IpcProtocol.ECommandType type, T data = default) where T : struct {
        if ( !m_running )
            return;

        int dataLen = data.Equals(default(T)) ? 0 : Marshal.SizeOf<T>();
        int bufferLen = Marshal.SizeOf<IpcProtocol.CommandHeader>() + dataLen;
        byte[] buffer = new byte[bufferLen];

        IpcProtocol.CommandHeader header = new IpcProtocol.CommandHeader
        {
            type = type,
            dataLen = dataLen
        };

        IntPtr headerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<IpcProtocol.CommandHeader>());
        Marshal.StructureToPtr(header, headerPtr, false);
        Marshal.Copy(headerPtr, buffer, 0, Marshal.SizeOf<IpcProtocol.CommandHeader>());
        Marshal.FreeHGlobal(headerPtr);

        if ( dataLen > 0 ) {
            IntPtr dataPtr = Marshal.AllocHGlobal(dataLen);
            Marshal.StructureToPtr(data, dataPtr, false);
            Marshal.Copy(dataPtr, buffer, Marshal.SizeOf<IpcProtocol.CommandHeader>(), dataLen);
            Marshal.FreeHGlobal(dataPtr);
        }

        m_stream.Write(buffer, 0, buffer.Length);
    }

        // no data
    private void SendIpcCommand(IpcProtocol.ECommandType type) {
        if ( !m_running )
            return;

        int bufferLen = Marshal.SizeOf<IpcProtocol.CommandHeader>();
        byte[] buffer = new byte[bufferLen];

        IpcProtocol.CommandHeader header = new IpcProtocol.CommandHeader
        {
            type = type,
            dataLen = 0
        };

        IntPtr headerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<IpcProtocol.CommandHeader>());
        Marshal.StructureToPtr(header, headerPtr, false);
        Marshal.Copy(headerPtr, buffer, 0, Marshal.SizeOf<IpcProtocol.CommandHeader>());
        Marshal.FreeHGlobal(headerPtr);

        m_stream.Write(buffer, 0, buffer.Length);
    }

    private T ByteArrayToStructure<T>(byte[] bytes, int offset) where T : struct {
        int size = Marshal.SizeOf<T>();
        if ( size > bytes.Length - offset ) {
            throw new ArgumentException("Byte array is too small to contain the structure.");
        }

        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.Copy(bytes, offset, ptr, size);
        T structure = (T)Marshal.PtrToStructure(ptr, typeof(T));
        Marshal.FreeHGlobal(ptr);

        return structure;
    }

    public IpcProtocol.CommandDataServerGazeDataResult RequestEyeTrackingData() {

        if ( !m_running ) {
            return new IpcProtocol.CommandDataServerGazeDataResult();
        }

        return m_lastGazeState ?? new IpcProtocol.CommandDataServerGazeDataResult();
    }

    public void TriggerEffectDisable(IpcProtocol.EVRControllerType controllerType) {
        if ( !m_running ) {
            return;
        }

        IpcProtocol.CommandDataClientTriggerEffectOff effectOff = new IpcProtocol.CommandDataClientTriggerEffectOff() {
            controllerType = controllerType
        };
        SendIpcCommand(IpcProtocol.ECommandType.ClientTriggerEffectOff, effectOff);
    }

    public void TriggerEffectFeedback(IpcProtocol.EVRControllerType controllerType, byte position, byte strength) {
        if ( !m_running ) {
            return;
        }

        IpcProtocol.CommandDataClientTriggerEffectFeedback effectFeedback = new IpcProtocol.CommandDataClientTriggerEffectFeedback() {
            controllerType = controllerType,
            position = position,
            strength = strength,
        };
        SendIpcCommand(IpcProtocol.ECommandType.ClientTriggerEffectFeedback, effectFeedback);
    }
    public void TriggerEffectWeapon(IpcProtocol.EVRControllerType controllerType, byte startPosition, byte endPosition, byte strength) {
        if ( !m_running ) {
            return;
        }

        IpcProtocol.CommandDataClientTriggerEffectWeapon effectWeapon = new IpcProtocol.CommandDataClientTriggerEffectWeapon() {
            controllerType = controllerType,
            startPosition = startPosition,
            endPosition = endPosition,
            strength = strength,
        };
        SendIpcCommand(IpcProtocol.ECommandType.ClientTriggerEffectWeapon, effectWeapon);
    }
    public void TriggerEffectVibration(IpcProtocol.EVRControllerType controllerType, byte position, byte amplitude, byte frequency) {
        if ( !m_running ) {
            return;
        }

        IpcProtocol.CommandDataClientTriggerEffectVibration effectVibration = new IpcProtocol.CommandDataClientTriggerEffectVibration() {
            controllerType = controllerType,
            position = position,
            amplitude = amplitude,
            frequency = frequency,
        };
        SendIpcCommand(IpcProtocol.ECommandType.ClientTriggerEffectVibration, effectVibration);
    }
    public void TriggerEffectMultiplePositionFeedback(IpcProtocol.EVRControllerType controllerType, byte[] strength) {
        if ( !m_running ) {
            return;
        }

        IpcProtocol.CommandDataClientTriggerEffectMultiplePositionFeedback effectVibration = new IpcProtocol.CommandDataClientTriggerEffectMultiplePositionFeedback() {
            controllerType = controllerType,
            strength = strength,
        };
        SendIpcCommand(IpcProtocol.ECommandType.ClientTriggerEffectMultiplePositionFeedback, effectVibration);
    }
    public void TriggerEffectSlopeFeedback(IpcProtocol.EVRControllerType controllerType, byte startPosition, byte endPosition, byte startStrength, byte endStrength) {
        if ( !m_running ) {
            return;
        }

        IpcProtocol.CommandDataClientTriggerEffectSlopeFeedback effectVibration = new IpcProtocol.CommandDataClientTriggerEffectSlopeFeedback() {
            controllerType = controllerType,
            startPosition = startPosition,
            endPosition = endPosition,
            startStrength = startStrength,
            endStrength = endStrength,
        };
        SendIpcCommand(IpcProtocol.ECommandType.ClientTriggerEffectSlopeFeedback, effectVibration);
    }

    public void TriggerEffectMultiplePositionVibration(IpcProtocol.EVRControllerType controllerType, byte frequency,
	    byte[] amplitude) {
	    if (!m_running) {
		    return;
	    }

	    IpcProtocol.CommandDataClientTriggerEffectMultiplePositionVibration effectVibration =
		    new IpcProtocol.CommandDataClientTriggerEffectMultiplePositionVibration() {
			    controllerType = controllerType, frequency = frequency, amplitude = amplitude,
		    };
	    SendIpcCommand(IpcProtocol.ECommandType.ClientTriggerEffectMultiplePositionVibration, effectVibration);
    }
}
