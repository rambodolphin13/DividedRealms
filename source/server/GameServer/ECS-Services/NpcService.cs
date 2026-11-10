using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DOL.AI;
using DOL.Logging;
using DOL.Timing;
using ECS.Debug;

namespace DOL.GS
{
    public sealed class NpcService : GameServiceBase
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private List<ABrain> _list;

        public static NpcService Instance { get; }

        static NpcService()
        {
            Instance = new();
        }

        public override void Tick()
        {
            // Serial steps before the parallel brain dispatch are timed for SERVER_STALL (DR-203).
            long step = AiTurnStallMonitor.SerialStart();
            ProcessPostedActionsParallel();
            AiTurnStallMonitor.EndSerial("NpcService.PostedActions", step);
            step = AiTurnStallMonitor.SerialStart();
            AutonomousBotRegistry.PrepareBrainTick();
            AiTurnStallMonitor.EndSerial("BotRegistry.PrepareBrainTick", step);
            AutonomousWorldBotController.PrepareRvrPlanningTick();
            step = AiTurnStallMonitor.SerialStart();
            AutonomousBotGroupCoordinator.PrepareCoordinatorTick();
            AiTurnStallMonitor.EndSerial("GroupCoordinator.PrepareCoordinatorTick", step);
            step = AiTurnStallMonitor.SerialStart();
            AutonomousWorldBotController.PrepareCampPlanningTick();
            AiTurnStallMonitor.EndSerial("CampPlanning.PrepareCampPlanningTick", step);
            int lastValidIndex;

            try
            {
                _list = ServiceObjectStore.UpdateAndGetAll<ABrain>(ServiceObjectType.Brain, out lastValidIndex);
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error($"{nameof(ServiceObjectStore.UpdateAndGetAll)} failed. Skipping this tick.", e);

                return;
            }

            step = AiTurnStallMonitor.SerialStart();
            GameLoop.ExecuteForEach(_list, lastValidIndex + 1, TickInternal);
            // The whole parallel dispatch; compare it with the slowest single turn in the same SERVER_STALL line.
            AiTurnStallMonitor.EndSerial("NpcService.BrainDispatch", step);

            if (Diagnostics.CheckServiceObjectCount)
                Diagnostics.PrintServiceObjectCount(ServiceName, ref EntityCount, _list.Count);
        }

        private static void TickInternal(ABrain brain)
        {
            try
            {
                if (Diagnostics.CheckServiceObjectCount)
                    Interlocked.Increment(ref Instance.EntityCount);

                GameNPC npc = brain.Body;

                if (GameServiceUtils.ShouldTick(brain.NextThinkTick))
                {
                    if (!brain.IsActive)
                    {
                        brain.Stop();
                        return;
                    }

                    long startTick = MonotonicTime.NowMs;
                    NavQueryProfile.Reset();
                    brain.Think();
                    long stopTick = MonotonicTime.NowMs;

                    long elapsed = stopTick - startTick;
                    if (elapsed > Diagnostics.LongTickThreshold)
                    {
                        // Every slow turn feeds the once-a-minute SERVER_STALL summary;
                        // only big ones still get their own warning line.
                        AiTurnStallMonitor.Record(npc.Name, brain.GetType().Name, elapsed);
                        if (AiTurnStallMonitor.ShouldLogIndividually(elapsed))
                            log.Warn($"Long {Instance.ServiceName}.{nameof(Tick)} for {npc.Name}({npc.ObjectID}) Interval: {brain.ThinkInterval} BrainType: {brain.GetType()} Time: {elapsed}ms{NavQueryProfile.Summary()}{(npc is GameBot slowBot ? $" activity=\"{slowBot.PersistentRecord?.Activity}\" goal=\"{slowBot.PersistentRecord?.ObjectiveKind}\" phase=\"{slowBot.PersistentRecord?.ObjectivePhase}\"" : string.Empty)}");
                    }

                    brain.NextThinkTick = GameLoop.GameLoopTime + brain.ThinkInterval;
                }
            }
            catch (Exception e)
            {
                GameServiceUtils.HandleServiceException(e, Instance.ServiceName, brain, brain.Body);
            }
        }
    }
}
