using System;
// 模块：核心测试 / 通讯运行时。
// 职责范围：固化TCP双端点、共享监听、远端会话路由、本地绑定和自动重连行为。

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class CommunicationHubTests
    {
        [ClassInitialize]
        public static void InitializeRuntimeConfiguration(TestContext context)
        {
            Assert.IsTrue(AppConfigStorage.TryLoad(out _, out string error), error);
        }

        [TestMethod]
        public void SocketValidation_AllowsSharedListenerAndRejectsAmbiguousCatchAll()
        {
            int listenPort = GetFreeTcpPort();
            var store = new CommunicationConfigStore();
            var first = CreateServer("前机台", 1, listenPort, GetFreeTcpPort());
            var second = CreateServer("后机台", 2, listenPort, GetFreeTcpPort());

            Assert.IsTrue(store.ReplaceSockets(new[] { first, second }, out string error), error);

            first.RemoteAddress = "*";
            first.RemotePort = 0;
            second.RemoteAddress = "*";
            second.RemotePort = 0;
            Assert.IsFalse(store.ReplaceSockets(new[] { first, second }, out error));
            StringAssert.Contains(error, "只能配置一个未匹配客户端接收通道");
        }

        [TestMethod]
        public void SocketValidation_RejectsOverlappingLocalBindings()
        {
            int serverPort = GetFreeTcpPort();
            var store = new CommunicationConfigStore();
            SocketInfo anyServer = CreateServer("全部网卡", 1, serverPort, GetFreeTcpPort());
            anyServer.LocalAddress = IPAddress.Any.ToString();
            SocketInfo loopbackServer = CreateServer("回环网卡", 2, serverPort, GetFreeTcpPort());

            Assert.IsFalse(store.ReplaceSockets(new[] { anyServer, loopbackServer }, out string error));
            StringAssert.Contains(error, "监听地址重叠");

            int clientPort = GetFreeTcpPort();
            SocketInfo anyClient = CreateClient("客户端一", 3, GetFreeTcpPort(), clientPort, false);
            anyClient.LocalAddress = IPAddress.Any.ToString();
            SocketInfo loopbackClient = CreateClient("客户端二", 4, GetFreeTcpPort(), clientPort, false);
            Assert.IsFalse(store.ReplaceSockets(new[] { anyClient, loopbackClient }, out error));
            StringAssert.Contains(error, "本地绑定地址冲突");
        }

        [TestMethod]
        public async Task ServerChannels_SharingListener_RouteSendAndReceiveByRemoteEndpoint()
        {
            int listenPort = GetFreeTcpPort();
            int firstClientPort = GetFreeTcpPort();
            int secondClientPort = GetFreeTcpPort();
            using (var hub = new CommunicationHub())
            using (var firstClient = CreateBoundClient(firstClientPort))
            using (var secondClient = CreateBoundClient(secondClientPort))
            {
                await hub.StartTcpAsync(CreateServer("前机台", 1, listenPort, firstClientPort));
                await hub.StartTcpAsync(CreateServer("后机台", 2, listenPort, secondClientPort));

                await firstClient.ConnectAsync(IPAddress.Loopback, listenPort);
                await secondClient.ConnectAsync(IPAddress.Loopback, listenPort);
                await WaitUntilAsync(() => hub.IsTcpActive("前机台") && hub.IsTcpActive("后机台"), 3000);

                Assert.IsTrue(await hub.SendTcpAsync("前机台", "A", false));
                Assert.AreEqual("A", await ReadTextAsync(firstClient, 2000));
                Assert.IsFalse(secondClient.GetStream().DataAvailable, "定向发送不应广播到其他逻辑会话。");

                byte[] reply = Encoding.UTF8.GetBytes("B");
                await secondClient.GetStream().WriteAsync(reply, 0, reply.Length);
                CommReceiveResult received = await hub.ReceiveTcpAsync("后机台", 2000);
                Assert.IsTrue(received.Success, received.ErrorMessage);
                Assert.AreEqual("B", received.MessageText);
                StringAssert.Contains(received.RemoteEndPoint, secondClientPort.ToString());
            }
        }

        [TestMethod]
        public async Task ServerChannel_RejectsClientOutsideConfiguredRemoteEndpoint()
        {
            int listenPort = GetFreeTcpPort();
            int expectedClientPort = GetFreeTcpPort();
            int unexpectedClientPort = GetFreeTcpPort();
            using (var hub = new CommunicationHub())
            using (var unexpectedClient = CreateBoundClient(unexpectedClientPort))
            {
                await hub.StartTcpAsync(CreateServer("指定客户端", 1, listenPort, expectedClientPort));
                await unexpectedClient.ConnectAsync(IPAddress.Loopback, listenPort);

                byte[] buffer = new byte[1];
                int count = await WithTimeout(unexpectedClient.GetStream().ReadAsync(buffer, 0, 1), 2000);
                Assert.AreEqual(0, count, "不符合远端筛选条件的连接应由共享监听器立即关闭。");
                Assert.IsFalse(hub.IsTcpActive("指定客户端"));
                Assert.AreEqual(TcpConnectionState.Listening,
                    hub.GetTcpStatus("指定客户端").ConnectionState);
            }
        }

        [TestMethod]
        public async Task ClientChannel_BindsConfiguredLocalEndpoint()
        {
            int serverPort = GetFreeTcpPort();
            int localPort = GetFreeTcpPort();
            TcpListener listener = CreateListener(serverPort);
            try
            {
                using (var hub = new CommunicationHub())
                {
                    Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
                    await hub.StartTcpAsync(CreateClient("固定源端口", 1, serverPort, localPort, false));
                    using (TcpClient accepted = await WithTimeout(acceptTask, 3000))
                    {
                        var remote = (IPEndPoint)accepted.Client.RemoteEndPoint;
                        Assert.AreEqual(IPAddress.Loopback, remote.Address);
                        Assert.AreEqual(localPort, remote.Port);
                        Assert.IsTrue(hub.IsTcpActive("固定源端口"));
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task ClientChannel_AutoReconnectsAfterInitialFailureAndEstablishedDisconnect()
        {
            int serverPort = GetFreeTcpPort();
            int localPort = GetFreeTcpPort();
            using (var hub = new CommunicationHub())
            {
                await hub.StartTcpAsync(CreateClient("自动重连", 1, serverPort, localPort, true));
                TcpStatus reconnecting = hub.GetTcpStatus("自动重连");
                Assert.IsTrue(reconnecting.IsStarted);
                Assert.IsFalse(reconnecting.IsConnected);
                Assert.AreEqual(TcpConnectionState.Reconnecting, reconnecting.ConnectionState);

                TcpListener listener = CreateListener(serverPort);
                try
                {
                    using (TcpClient accepted = await WithTimeout(listener.AcceptTcpClientAsync(), 5000))
                    {
                        await WaitUntilAsync(() => hub.IsTcpActive("自动重连"), 3000);
                        Assert.AreEqual(localPort, ((IPEndPoint)accepted.Client.RemoteEndPoint).Port);
                        Assert.AreEqual(TcpConnectionState.Connected,
                            hub.GetTcpStatus("自动重连").ConnectionState);
                    }
                }
                finally
                {
                    listener.Stop();
                }

                await WaitUntilAsync(() => hub.GetTcpStatus("自动重连").ConnectionState
                    == TcpConnectionState.Reconnecting, 3000);

                TcpListener restartedListener = CreateListener(serverPort);
                try
                {
                    using (TcpClient reconnected = await WithTimeout(restartedListener.AcceptTcpClientAsync(), 5000))
                    {
                        await WaitUntilAsync(() => hub.IsTcpActive("自动重连"), 3000);
                        Assert.AreEqual(localPort, ((IPEndPoint)reconnected.Client.RemoteEndPoint).Port);
                        Assert.IsTrue(reconnected.Connected);
                    }
                }
                finally
                {
                    restartedListener.Stop();
                }
            }
        }

        private static SocketInfo CreateServer(string name, int id, int localPort, int remotePort)
        {
            return new SocketInfo
            {
                ID = id,
                Name = name,
                Type = "Server",
                LocalAddress = IPAddress.Loopback.ToString(),
                LocalPort = localPort,
                RemoteAddress = IPAddress.Loopback.ToString(),
                RemotePort = remotePort,
                AutoReconnect = false,
                ConnectTimeoutMs = 500
            };
        }

        private static SocketInfo CreateClient(string name, int id, int remotePort, int localPort, bool autoReconnect)
        {
            return new SocketInfo
            {
                ID = id,
                Name = name,
                Type = "Client",
                LocalAddress = IPAddress.Loopback.ToString(),
                LocalPort = localPort,
                RemoteAddress = IPAddress.Loopback.ToString(),
                RemotePort = remotePort,
                AutoReconnect = autoReconnect,
                ConnectTimeoutMs = 300
            };
        }

        private static TcpClient CreateBoundClient(int localPort)
        {
            var client = new TcpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Loopback, localPort));
            return client;
        }

        private static TcpListener CreateListener(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            return listener;
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline) Assert.Fail($"等待条件超过{timeoutMs}ms仍未满足。");
                await Task.Delay(20);
            }
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (!ReferenceEquals(completed, task)) Assert.Fail($"异步操作超过{timeoutMs}ms仍未完成。");
            return await task;
        }

        private static async Task<string> ReadTextAsync(TcpClient client, int timeoutMs)
        {
            byte[] buffer = new byte[32];
            Task<int> readTask = client.GetStream().ReadAsync(buffer, 0, buffer.Length);
            int count = await WithTimeout(readTask, timeoutMs);
            return Encoding.UTF8.GetString(buffer, 0, count);
        }
    }
}
