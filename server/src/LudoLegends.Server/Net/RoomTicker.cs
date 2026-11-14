using System;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Core;
using LudoLegends.Server.Config;
using LudoLegends.Server.Economy;
using LudoLegends.Server.Rooms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LudoLegends.Server.Net
{
    /// <summary>
    /// Drives time-based behaviour once per second: turn deadlines, disconnected players,
    /// room cleanup and match settlement. Running this in one background service (instead
    /// of per-room timers) keeps the server cheap on a single small VM.
    /// </summary>
    public sealed class RoomTicker : BackgroundService
    {
        private readonly RoomManager _rooms;
        private readonly GameHub _hub;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<RoomTicker> _log;

        public RoomTicker(RoomManager rooms, GameHub hub, IServiceScopeFactory scopes, ILogger<RoomTicker> log)
        {
            _rooms = rooms;
            _hub = hub;
            _scopes = scopes;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    TickReport report = _rooms.Tick(DateTime.UtcNow);

                    foreach (var broadcast in report.Broadcasts)
                    {
                        await _hub.BroadcastRoomAsync(broadcast.room, broadcast.envelopes, stoppingToken);
                    }

                    foreach (MatchOutcome outcome in report.Outcomes)
                    {
                        using IServiceScope scope = _scopes.CreateScope();
                        MatchService matches = scope.ServiceProvider.GetRequiredService<MatchService>();
                        await matches.RecordAsync(outcome, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Room tick failed");
                }

                try
                {
                    await Task.Delay(1000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
