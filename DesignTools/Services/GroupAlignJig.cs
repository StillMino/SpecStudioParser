using HostMgd.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.Geometry;
using Teigha.GraphicsInterface;

namespace SpecStudioParser.DesignTools.Services
{
    public sealed class GroupAlignJig : DrawJig
    {
        private readonly IReadOnlyList<AlignmentPoint> _currentPoints;
        private readonly LeaderAlignmentAxis _axis;

        private AlignmentPoint _anchor;
        private double _step;
        private IReadOnlyList<AlignmentPoint> _targetPoints = Array.Empty<AlignmentPoint>();
        private int _collisionCount;

        private enum Phase { Anchor, Step }
        private Phase _phase = Phase.Anchor;

        public double FinalStep => _step;
        public AlignmentPoint FinalAnchor => _anchor;
        public bool WasAccepted { get; private set; }

        public GroupAlignJig(IReadOnlyList<AlignmentPoint> currentPoints, LeaderAlignmentAxis axis)
        {
            _currentPoints = currentPoints;
            _axis = axis;
            if (_currentPoints.Count > 0)
                _anchor = _currentPoints[0];
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            return _phase == Phase.Anchor ? SampleAnchor(prompts) : SampleStep(prompts);
        }

        private SamplerStatus SampleAnchor(JigPrompts prompts)
        {
            var promptOpts = new JigPromptPointOptions("\nУкажите опорную точку или выноску [Enter — первая]: ")
            {
                UserInputControls = UserInputControls.NullResponseAccepted
            };

            var result = prompts.AcquirePoint(promptOpts);

            if (result.Status == PromptStatus.None || result.Status == PromptStatus.Cancel)
            {
                if (_currentPoints.Count > 0)
                    _anchor = _currentPoints[0];
                _phase = Phase.Step;
                return SamplerStatus.NoChange;
            }

            if (result.Status == PromptStatus.OK)
            {
                _anchor = new AlignmentPoint(result.Value.X, result.Value.Y, result.Value.Z);
                _phase = Phase.Step;
                return SamplerStatus.NoChange;
            }

            WasAccepted = false;
            return SamplerStatus.Cancel;
        }

        private SamplerStatus SampleStep(JigPrompts prompts)
        {
            var distOpts = new JigPromptDistanceOptions("\nУкажите шаг [двигайте мышь / число / Enter]: ")
            {
                BasePoint = new Point3d(_anchor.X, _anchor.Y, _anchor.Z),
                UseBasePoint = true,
                UserInputControls = UserInputControls.NullResponseAccepted
            };

            var result = prompts.AcquireDistance(distOpts);

            if (result.Status == PromptStatus.None || result.Status == PromptStatus.Cancel)
            {
                if (Math.Abs(_step) < 1e-9)
                {
                    WasAccepted = false;
                    return SamplerStatus.Cancel;
                }
                WasAccepted = true;
                return SamplerStatus.OK;
            }

            if (result.Status == PromptStatus.Keyword)
            {
                WasAccepted = false;
                return SamplerStatus.Cancel;
            }

            if (result.Status == PromptStatus.OK)
            {
                _step = result.Value;
                ComputeTargets();
                return SamplerStatus.OK;
            }

            return SamplerStatus.NoChange;
        }

        private void ComputeTargets()
        {
            var ordered = _axis == LeaderAlignmentAxis.Horizontal
                ? _currentPoints.OrderBy(p => Math.Abs(p.X - _anchor.X)).ToArray()
                : _currentPoints.OrderBy(p => Math.Abs(p.Y - _anchor.Y)).ToArray();

            var targets = new AlignmentPoint[ordered.Length];
            for (var i = 0; i < ordered.Length; i++)
            {
                var p = ordered[i];
                targets[i] = _axis == LeaderAlignmentAxis.Horizontal
                    ? new AlignmentPoint(_anchor.X + _step * i, _anchor.Y, p.Z)
                    : new AlignmentPoint(_anchor.X, _anchor.Y + _step * i, p.Z);
            }
            _targetPoints = targets;
            _collisionCount = DetectJigCollisions(_targetPoints);
        }

        private static int DetectJigCollisions(IReadOnlyList<AlignmentPoint> points)
        {
            const double textWidth = 40.0;
            const double textHeight = 8.0;
            var count = 0;
            for (var i = 0; i < points.Count; i++)
            {
                for (var j = i + 1; j < points.Count; j++)
                {
                    var a = points[i]; var b = points[j];
                    var spacing = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
                    if (Math.Abs(a.X - b.X) < 0.1 && spacing < textHeight) count++;
                    else if (Math.Abs(a.Y - b.Y) < 0.1 && spacing < textWidth * 0.6) count++;
                }
            }
            return count;
        }

        protected override bool WorldDraw(WorldDraw wd)
        {
            if (_targetPoints.Count == 0) return true;
            var sub = wd.SubEntityTraits;

            for (var i = 0; i < _targetPoints.Count && i < _currentPoints.Count; i++)
            {
                var from = _currentPoints[i];
                var to = _targetPoints[i];

                sub.Color = 5;
                wd.Geometry.WorldLine(new Point3d(from.X, from.Y, from.Z), new Point3d(to.X, to.Y, to.Z));

                sub.Color = 3;
                wd.Geometry.WorldLine(new Point3d(_anchor.X, _anchor.Y, _anchor.Z), new Point3d(to.X, to.Y, to.Z));

                sub.Color = 1;
                var half = Math.Max(Math.Abs(_step) * 0.3, 2.0);
                wd.Geometry.WorldLine(new Point3d(to.X - half, to.Y, to.Z), new Point3d(to.X + half, to.Y, to.Z));
                wd.Geometry.WorldLine(new Point3d(to.X, to.Y - half, to.Z), new Point3d(to.X, to.Y + half, to.Z));
            }

            if (_collisionCount > 0)
            {
                sub.Color = 1;
                for (var i = 0; i < _targetPoints.Count; i++)
                    DrawWorldCircle(wd, _targetPoints[i], 5.0);
            }

            return true;
        }

        private static void DrawWorldCircle(WorldDraw wd, AlignmentPoint center, double radius)
        {
            const int segments = 12;
            var pts = new Point3d[segments];
            for (var i = 0; i < segments; i++)
            {
                var angle = 2.0 * Math.PI * i / segments;
                pts[i] = new Point3d(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle), center.Z);
            }
            for (var i = 0; i < segments; i++)
                wd.Geometry.WorldLine(pts[i], pts[(i + 1) % segments]);
        }

        public struct AlignmentPoint
        {
            public double X, Y, Z;
            public AlignmentPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        }
    }
}
