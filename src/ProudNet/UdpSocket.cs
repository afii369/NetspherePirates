﻿using System;
using System.Net;
using System.Threading.Tasks;
using BlubLib;
using BlubLib.Serialization;
using BlubLib.Threading.Tasks;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProudNet.Codecs;
using ProudNet.Configuration;
using ProudNet.Handlers;
using ProudNet.Serialization.Messages.Core;

namespace ProudNet
{
    internal class UdpSocket : IDisposable
    {
        private readonly NetworkOptions _options;
        private readonly IServiceProvider _serviceProvider;

        private bool _disposed;
        private IEventLoopGroup _eventLoopGroup;

        public IChannel Channel { get; private set; }

        public UdpSocket(IOptions<NetworkOptions> options, IServiceProvider serviceProvider)
        {
            _options = options.Value;
            _serviceProvider = serviceProvider;
        }

        public void Listen(IPEndPoint endPoint, IEventLoopGroup eventLoopGroup)
        {
            ThrowIfDisposed();

            if (eventLoopGroup == null)
                throw new ArgumentNullException(nameof(eventLoopGroup));

            _eventLoopGroup = eventLoopGroup;

            try
            {
                Channel = new Bootstrap()
                    .Group(_eventLoopGroup ?? eventLoopGroup)
                    .Channel<SocketDatagramChannel>()
                    .Handler(new ActionChannelInitializer<IChannel>(ch =>
                    {
                        ch.Pipeline
                            .AddLast(new UdpFrameDecoder((int)_options.MessageMaxLength))
                            .AddLast(new UdpFrameEncoder())
                            .AddLast(_serviceProvider.GetService<UdpHandler>())
                            .AddLast(_serviceProvider.GetService<ErrorHandler>());
                    }))
                    .BindAsync(endPoint).WaitEx();
            }
            catch (Exception ex)
            {
                _eventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)).WaitEx();
                _eventLoopGroup = null;
                Channel = null;
                ex.Rethrow();
            }
        }

        public Task SendAsync(ICoreMessage message, IPEndPoint endPoint)
        {
            return Channel.WriteAndFlushAsync(new SendContext { Message = message, UdpEndPoint = endPoint });
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _eventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)).WaitEx();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }
    }
}
