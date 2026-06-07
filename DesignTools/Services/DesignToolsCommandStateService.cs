using System;

namespace SpecStudioParser.DesignTools.Services
{
    public enum DesignToolsToolKind
    {
        Leaders,
        Dimensions,
        Diagnostics
    }

    public enum DesignToolsLeaderSource
    {
        MultiCad,
        TeighaMLeader
    }

    public enum DesignToolsOperation
    {
        Align,
        Distribute,
        Shift,
        Reset,
        Check
    }

    public enum DesignToolsReferenceMode
    {
        First,
        Point
    }

    public enum DesignToolsDiagnosticsSource
    {
        AllObjects,
        Dimensions
    }

    public sealed class DesignToolsCommandState
    {
        public DesignToolsToolKind ToolKind { get; init; }
        public DesignToolsLeaderSource LeaderSource { get; init; } = DesignToolsLeaderSource.TeighaMLeader;
        public DesignToolsOperation Operation { get; init; } = DesignToolsOperation.Align;
        public LeaderAlignmentAxis Axis { get; init; } = LeaderAlignmentAxis.Horizontal;
        public DesignToolsReferenceMode ReferenceMode { get; init; } = DesignToolsReferenceMode.First;
        public DesignToolsDiagnosticsSource DiagnosticsSource { get; init; } = DesignToolsDiagnosticsSource.AllObjects;
    }

    public sealed class DesignToolsCommandResultEventArgs : EventArgs
    {
        public DesignToolsToolKind ToolKind { get; }
        public string Message { get; }

        public DesignToolsCommandResultEventArgs(DesignToolsToolKind toolKind, string message)
        {
            ToolKind = toolKind;
            Message = message;
        }
    }

    public static class DesignToolsCommandStateService
    {
        private static readonly object SyncRoot = new();
        private static DesignToolsCommandState? _pendingState;

        public static event EventHandler<DesignToolsCommandResultEventArgs>? ResultPublished;

        public static void SetPendingState(DesignToolsCommandState state)
        {
            lock (SyncRoot)
            {
                _pendingState = state;
            }
        }

        public static DesignToolsCommandState? GetPendingState(DesignToolsToolKind expectedToolKind)
        {
            lock (SyncRoot)
            {
                if (_pendingState == null || _pendingState.ToolKind != expectedToolKind)
                {
                    return null;
                }

                return _pendingState;
            }
        }

        public static void PublishResult(DesignToolsToolKind toolKind, string message)
        {
            ResultPublished?.Invoke(null, new DesignToolsCommandResultEventArgs(toolKind, message));
        }
    }
}
