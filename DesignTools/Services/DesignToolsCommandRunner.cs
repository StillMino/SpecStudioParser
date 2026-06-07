using System;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    public sealed class DesignToolsCommandRunner
    {
        private readonly MultiCadLeaderAlignmentService _leaderAlignmentService = new();
        private readonly LeaderPointAlignmentService _leaderPointAlignmentService = new();
        private readonly DimensionAlignmentService _dimensionAlignmentService = new();
        private readonly DimensionDiagnosticsService _dimensionDiagnosticsService = new();
        private readonly SelectionDiagnosticsService _selectionDiagnosticsService = new();

        public string RunLeaders(DesignToolsCommandState state)
        {
            var result = state.Operation == DesignToolsOperation.Distribute
                ? ExecuteLeaderDistribution(state.LeaderSource, state.Axis)
                : state.ReferenceMode == DesignToolsReferenceMode.Point
                    ? ExecuteLeaderPointAlignment(state.LeaderSource, state.Axis)
                    : ExecuteLeaderAlignment(state.LeaderSource, state.Axis);

            WriteToNanoCad($"\n[DesignTools]: {result.Message}\n");
            DesignToolsCommandStateService.PublishResult(DesignToolsToolKind.Leaders, result.Message);
            return result.Message;
        }

        public string RunDimensions(DesignToolsCommandState state)
        {
            var result = state.Operation switch
            {
                DesignToolsOperation.Reset => _dimensionAlignmentService.ResetSelectedDimensionTextPositions(),
                DesignToolsOperation.Distribute => _dimensionAlignmentService.DistributeSelectedDimensions(state.Axis),
                _ => state.ReferenceMode == DesignToolsReferenceMode.Point
                    ? _dimensionAlignmentService.AlignSelectedDimensionsToPoint(state.Axis)
                    : _dimensionAlignmentService.AlignSelectedDimensions(state.Axis)
            };

            WriteToNanoCad($"\n[DesignTools]: {result.Message}\n");
            DesignToolsCommandStateService.PublishResult(DesignToolsToolKind.Dimensions, result.Message);
            return result.Message;
        }

        public string RunDiagnostics(DesignToolsCommandState state)
        {
            if (state.DiagnosticsSource == DesignToolsDiagnosticsSource.Dimensions)
            {
                var result = _dimensionDiagnosticsService.DiagnoseSelectedDimensions();
                WriteToNanoCad("\n" + result.Details + "\n");
                DesignToolsCommandStateService.PublishResult(DesignToolsToolKind.Diagnostics, result.Summary);
                return result.Summary;
            }

            var selectionResult = _selectionDiagnosticsService.DiagnoseSelection();
            WriteToNanoCad("\n" + selectionResult.Details + "\n");
            DesignToolsCommandStateService.PublishResult(DesignToolsToolKind.Diagnostics, selectionResult.Summary);
            return selectionResult.Summary;
        }

        private LeaderAlignmentResult ExecuteLeaderAlignment(DesignToolsLeaderSource source, LeaderAlignmentAxis axis)
        {
            return source switch
            {
                DesignToolsLeaderSource.MultiCad => _leaderAlignmentService.AlignSelectedMultiCadLeaders(axis),
                DesignToolsLeaderSource.TeighaMLeader => _leaderAlignmentService.AlignSelectedTeighaMLeaders(axis),
                _ => _leaderAlignmentService.AlignSelectedLeaders(axis)
            };
        }

        private LeaderAlignmentResult ExecuteLeaderDistribution(DesignToolsLeaderSource source, LeaderAlignmentAxis axis)
        {
            return source switch
            {
                DesignToolsLeaderSource.MultiCad => _leaderAlignmentService.DistributeSelectedMultiCadLeaders(axis),
                DesignToolsLeaderSource.TeighaMLeader => _leaderAlignmentService.DistributeSelectedTeighaMLeaders(axis),
                _ => TryAutoDistributeLeaders(axis)
            };
        }

        private LeaderAlignmentResult TryAutoDistributeLeaders(LeaderAlignmentAxis axis)
        {
            var multiCadResult = _leaderAlignmentService.DistributeSelectedMultiCadLeaders(axis);
            return multiCadResult.CandidateCount > 0
                ? multiCadResult
                : _leaderAlignmentService.DistributeSelectedTeighaMLeaders(axis);
        }

        private LeaderAlignmentResult ExecuteLeaderPointAlignment(DesignToolsLeaderSource source, LeaderAlignmentAxis axis)
        {
            if (source == DesignToolsLeaderSource.MultiCad)
            {
                return _leaderPointAlignmentService.AlignSelectedMultiCadLeadersToPoint(axis);
            }

            if (source == DesignToolsLeaderSource.TeighaMLeader)
            {
                return _leaderPointAlignmentService.AlignSelectedTeighaMLeadersToPoint(axis);
            }

            var multiCadResult = _leaderPointAlignmentService.AlignSelectedMultiCadLeadersToPoint(axis);
            return multiCadResult.CandidateCount > 0 || multiCadResult.AlignedCount > 0
                ? multiCadResult
                : _leaderPointAlignmentService.AlignSelectedTeighaMLeadersToPoint(axis);
        }

        private static void WriteToNanoCad(string message)
        {
            try { CadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage(message); } catch { }
        }
    }
}
