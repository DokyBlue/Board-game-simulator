using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BoardGameSimulator.Networking
{

    public readonly struct NetworkPacket
    {
        public readonly uint MsgCode;
        public readonly string Body;

        public NetworkPacket(uint msgCode, string body)
        {
            MsgCode = msgCode;
            Body = body;
        }
    }

    public class TcpNetworkManager : MonoBehaviour
    {

        // --- 单例模式 ---
        public static TcpNetworkManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this.gameObject); // 保证切换场景时网络不断开
        }

        private const int HeaderLength = 8;

        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 8086;

        private readonly ConcurrentQueue<NetworkPacket> receiveQueue = new ConcurrentQueue<NetworkPacket>();
        private readonly object sendLock = new object();

        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private Thread receiveThread;
        private CancellationTokenSource receiveCancellation;
        private volatile bool isConnected;

        public event Action<uint, string> OnPacketReceived;

        public bool IsConnected => isConnected && tcpClient != null && tcpClient.Connected;

        public async Task<bool> ConnectAsync()
        {
            Disconnect();

            tcpClient = new TcpClient();
            receiveCancellation = new CancellationTokenSource();

            try
            {
                await tcpClient.ConnectAsync(host, port);
                networkStream = tcpClient.GetStream();
                isConnected = true;

                receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "TcpNetworkManager.ReceiveLoop"
                };
                receiveThread.Start();

                Debug.Log($"TCP connected: {host}:{port}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"TCP connect failed: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            isConnected = false;

            if (receiveCancellation != null)
            {
                receiveCancellation.Cancel();
                receiveCancellation.Dispose();
                receiveCancellation = null;
            }

            if (networkStream != null)
            {
                networkStream.Close();
                networkStream = null;
            }

            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }

            if (receiveThread != null && receiveThread.IsAlive)
            {
                if (!receiveThread.Join(200))
                {
                    receiveThread.Interrupt();
                }
            }

            receiveThread = null;
        }

        public void SendMessage(uint msgCode, string jsonBody)
        {
            if (!IsConnected || networkStream == null)
            {
                Debug.LogWarning("TCP send ignored: not connected.");
                return;
            }

            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            var pkgLen = bodyBytes.Length;

            var header = new byte[HeaderLength];
            var pkgLenNet = IPAddress.HostToNetworkOrder(pkgLen);
            var msgCodeNet = IPAddress.HostToNetworkOrder(unchecked((int)msgCode));

            Buffer.BlockCopy(BitConverter.GetBytes(pkgLenNet), 0, header, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(msgCodeNet), 0, header, 4, 4);

            var packetBytes = new byte[HeaderLength + pkgLen];
            Buffer.BlockCopy(header, 0, packetBytes, 0, HeaderLength);
            if (pkgLen > 0)
            {
                Buffer.BlockCopy(bodyBytes, 0, packetBytes, HeaderLength, pkgLen);
            }

            lock (sendLock)
            {
                try
                {
                    networkStream.Write(packetBytes, 0, packetBytes.Length);
                    networkStream.Flush();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"TCP send failed: {ex.Message}");
                    Disconnect();
                }
            }
        }

        private void Update()
        {
            while (receiveQueue.TryDequeue(out var packet))
            {
                OnPacketReceived?.Invoke(packet.MsgCode, packet.Body);
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        private void ReceiveLoop()
        {
            var cancellation = receiveCancellation;
            if (cancellation == null)
            {
                return;
            }

            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var header = ReadExact(HeaderLength, cancellation.Token);
                    if (header == null)
                    {
                        break;
                    }

                    var pkgLen = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
                    var msgCodeInt = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 4));

                    if (pkgLen < 0)
                    {
                        Debug.LogError($"TCP receive invalid package length: {pkgLen}");
                        break;
                    }

                    var bodyBytes = pkgLen == 0 ? Array.Empty<byte>() : ReadExact(pkgLen, cancellation.Token);
                    if (bodyBytes == null)
                    {
                        break;
                    }

                    var body = bodyBytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bodyBytes);
                    receiveQueue.Enqueue(new NetworkPacket(unchecked((uint)msgCodeInt), body));
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal during disconnect.
            }
            catch (ThreadInterruptedException)
            {
                // Normal during forced shutdown.
            }
            catch (Exception ex)
            {
                Debug.LogError($"TCP receive loop error: {ex.Message}");
            }
            finally
            {
                isConnected = false;
            }
        }

        private byte[] ReadExact(int length, CancellationToken token)
        {
            if (networkStream == null)
            {
                return null;
            }

            var buffer = new byte[length];
            var offset = 0;

            while (offset < length)
            {
                if (token.IsCancellationRequested)
                {
                    return null;
                }

                var read = networkStream.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    return null;
                }

                offset += read;
            }

            return buffer;
        }
    }
}
