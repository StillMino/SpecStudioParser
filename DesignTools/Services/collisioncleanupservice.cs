using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    public sealed class CollisionCleanupService
    {
        private static readonly string[] LeaderTypeMarkers =
        {
            "McNote", "McNotePosition", "McNoteChain", "McNoteComb", "McNoteLinearMark",
            "McMark", "McAnnotation", "McSpecLeader", "MLeader", "MultiLeader", "Leader",
            "Callout", "Выноска", "Мультивыноска"
        };

        private static readonly string[] AnchorPointProperties =
        {
            "TextLocation", "BlockPosition", "Origin", "TextPosition", "TextPos", "PntText",
            "Start", "End", "Location", "Position"
        };

        /// <summary>
        /// Detect and resolve collisions among text objects and leaders.
        /// scope: "text" = DBText/MText only, "leaders" = MLeader + MultiCAD leaders, "all" = both
        /// </summary>
        public CollisionCleanupResult DetectAndResolveCollisions(double minDistance, string scope = "text")
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var db = doc?.Database;
            var editor = doc?.Editor;
            if (db == null || editor == null)
                return new CollisionCleanupResult { Message = "Нет активного документа." };

            var items = new List<CollisionItem>();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                if (scope == "text" || scope == "all")
                {
                    foreach (ObjectId id in ms)
                    {
                        if (id.ObjectClass.Name == "AcDbText")
                        {
                            var txt = (DBText)tx.GetObject(id, OpenMode.ForRead);
                            var ext = txt.GeometricExtents;
                            items.Add(new CollisionItem(id, ext.MinPoint, ext.MaxPoint, TextKind.DbText));
                        }
                        else if (id.ObjectClass.Name == "AcDbMText")
                        {
                            var mtxt = (MText)tx.GetObject(id, OpenMode.ForRead);
                            var ext = mtxt.GeometricExtents;
                            items.Add(new CollisionItem(id, ext.MinPoint, ext.MaxPoint, TextKind.MText));
                        }
                    }
                }

                if (scope == "leaders" || scope == "all")
                {
                    // Teigha MLeader from database
                    foreach (ObjectId id in ms)
                    {
                        if (id.ObjectClass.Name == "AcDbMLeader")
                        {
                            var ml = (MLeader)tx.GetObject(id, OpenMode.ForRead);
                            if (TryGetMLeaderTextBounds(ml, out var min, out var max))
                                items.Add(new CollisionItem(id, min, max, TextKind.MLeader));
                        }
                    }
                }

                tx.Commit();
            }

            // MultiCAD leaders via selection API (only for leaders/all scope)
            if (scope is "leaders" or "all")
            {
                var mcItems = TryCollectMultiCadLeaderItems();
                items.AddRange(mcItems);
            }

            if (items.Count < 2)
                return new CollisionCleanupResult { Message = $"Недостаточно объектов: {items.Count}.", TotalTexts = items.Count };

            // Поиск коллизий
            var collisionPairs = new List<(int A, int B)>();
            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    if (CalculateGap(items[i], items[j]) < minDistance)
                        collisionPairs.Add((i, j));
                }
            }

            if (collisionPairs.Count == 0)
                return new CollisionCleanupResult
                {
                    TotalTexts = items.Count,
                    CollisionsFound = 0,
                    Message = $"Коллизий не найдено. Проверено {items.Count} объектов, порог {minDistance:F1}."
                };

            // Union-Find
            var parent = new int[items.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
            void Union(int a, int b) => parent[Find(a)] = Find(b);

            foreach (var (a, b) in collisionPairs)
                Union(a, b);

            var clusters = new Dictionary<int, List<int>>();
            for (var i = 0; i < items.Count; i++)
            {
                var root = Find(i);
                if (!clusters.ContainsKey(root)) clusters[root] = new List<int>();
                clusters[root].Add(i);
            }

            var collisionRoots = new HashSet<int>();
            foreach (var (a, b) in collisionPairs)
                collisionRoots.Add(Find(a));

            var resolved = 0;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                foreach (var root in collisionRoots)
                {
                    var members = clusters[root];
                    if (members.Count < 2) continue;

                    var horizontal = IsHorizontalSpread(items, members);
                    var step = Math.Max(
                        members.Max(i => horizontal ? items[i].Width : items[i].Height) + minDistance,
                        minDistance
                    );

                    members.Sort((a, b) => horizontal
                        ? items[a].CenterX.CompareTo(items[b].CenterX)
                        : items[a].CenterY.CompareTo(items[b].CenterY));

                    var firstPos = horizontal ? items[members[0]].CenterX : items[members[0]].CenterY;
                    var lastPos = horizontal ? items[members[^1]].CenterX : items[members[^1]].CenterY;
                    var currentSpread = lastPos - firstPos;
                    var neededSpread = step * (members.Count - 1);

                    double startPos;
                    if (neededSpread <= currentSpread)
                    {
                        startPos = firstPos;
                        step = currentSpread / (members.Count - 1);
                        if (step < minDistance)
                        {
                            step = minDistance;
                            var mid = (firstPos + lastPos) / 2.0;
                            startPos = mid - step * (members.Count - 1) / 2.0;
                        }
                    }
                    else
                    {
                        var mid = (firstPos + lastPos) / 2.0;
                        startPos = mid - step * (members.Count - 1) / 2.0;
                    }

                    for (var idx = 0; idx < members.Count; idx++)
                    {
                        var item = items[members[idx]];
                        var targetPos = startPos + step * idx;
                        var currentPos = horizontal ? item.CenterX : item.CenterY;
                        var delta = targetPos - currentPos;
                        if (Math.Abs(delta) < 0.001) continue;

                        if (ApplyShift(tx, item, delta, horizontal))
                            resolved++;
                    }
                }

                tx.Commit();
            }

            return new CollisionCleanupResult
            {
                TotalTexts = items.Count,
                CollisionsFound = collisionPairs.Count,
                ResolvedCount = resolved,
                Message = $"Кластеров: {collisionRoots.Count}, пар: {collisionPairs.Count}, сдвинуто: {resolved}, порог: {minDistance:F1}."
            };
        }

        private static bool IsHorizontalSpread(List<CollisionItem> items, List<int> members)
        {
            var spreadX = members.Max(i => items[i].CenterX) - members.Min(i => items[i].CenterX);
            var spreadY = members.Max(i => items[i].CenterY) - members.Min(i => items[i].CenterY);
            return spreadX >= spreadY;
        }

        private static bool ApplyShift(Transaction tx, CollisionItem item, double delta, bool horizontal)
        {
            var obj = tx.GetObject(item.Id, OpenMode.ForWrite);
            if (obj == null) return false;

            switch (item.Kind)
            {
                case TextKind.DbText:
                {
                    var t = (DBText)obj;
                    var p = t.Position;
                    t.Position = horizontal
                        ? new Point3d(p.X + delta, p.Y, p.Z)
                        : new Point3d(p.X, p.Y + delta, p.Z);
                    return true;
                }
                case TextKind.MText:
                {
                    var t = (MText)obj;
                    var p = t.Location;
                    t.Location = horizontal
                        ? new Point3d(p.X + delta, p.Y, p.Z)
                        : new Point3d(p.X, p.Y + delta, p.Z);
                    return true;
                }
                case TextKind.MLeader:
                {
                    var ml = (MLeader)obj;
                    var tl = ml.TextLocation;
                    ml.TextLocation = horizontal
                        ? new Point3d(tl.X + delta, tl.Y, tl.Z)
                        : new Point3d(tl.X, tl.Y + delta, tl.Z);
                    return true;
                }
                case TextKind.MultiCadLeader:
                {
                    // Reflection-based shift: find anchor point property and shift it
                    return TryShiftMultiCadLeader(obj, delta, horizontal);
                }
                default:
                    return false;
            }
        }

        private static bool TryShiftMultiCadLeader(object obj, double delta, bool horizontal)
        {
            foreach (var propName in AnchorPointProperties)
            {
                var prop = obj.GetType().GetProperty(propName);
                if (prop?.CanRead == true && prop.CanWrite && TryGetPoint(prop.GetValue(obj), out var pt))
                {
                    var newPt = horizontal
                        ? Point3d.Origin + new Vector3d(pt.X + delta, pt.Y, pt.Z)
                        : Point3d.Origin + new Vector3d(pt.X, pt.Y + delta, pt.Z);
                    prop.SetValue(obj, newPt);
                    return true;
                }
            }

            // Try Get/Set methods
            foreach (var baseName in new[] { "TextLocation", "BlockPosition", "ContentLocation" })
            {
                var getMethod = obj.GetType().GetMethod("Get" + baseName);
                var setMethod = obj.GetType().GetMethod("Set" + baseName);
                if (getMethod != null && setMethod != null)
                {
                    var current = getMethod.Invoke(obj, null);
                    if (TryGetPoint(current, out var pt))
                    {
                        var newPt = horizontal
                            ? Point3d.Origin + new Vector3d(pt.X + delta, pt.Y, pt.Z)
                            : Point3d.Origin + new Vector3d(pt.X, pt.Y + delta, pt.Z);
                        setMethod.Invoke(obj, new object[] { newPt });
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetMLeaderTextBounds(MLeader ml, out Point3d min, out Point3d max)
        {
            try
            {
                var tl = ml.TextLocation;
                // Approximate bbox around text location
                min = new Point3d(tl.X - 5, tl.Y - 2, 0);
                max = new Point3d(tl.X + 25, tl.Y + 5, 0);
                return true;
            }
            catch
            {
                min = Point3d.Origin;
                max = Point3d.Origin;
                return false;
            }
        }

        private static List<CollisionItem> TryCollectMultiCadLeaderItems()
        {
            var result = new List<CollisionItem>();
            try
            {
                var mcAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Multicad");

                if (mcAssembly == null) return result;

                var mcManagerType = mcAssembly.GetType("Multicad.WindowManager")?
                    .GetProperty("Manager")?.GetValue(null);
                if (mcManagerType == null) return result;

                var selection = mcManagerType.GetType()
                    .GetMethod("GetSelection")?.Invoke(mcManagerType, null);
                if (selection == null) return result;

                var selectedObjects = (System.Collections.IEnumerable)(selection
                    .GetType().GetProperty("SelectedObjects")?.GetValue(selection) ?? new List<object>());

                foreach (var obj in selectedObjects)
                {
                    if (!IsLeaderCandidate(obj)) continue;
                    if (TryGetMultiCadLeaderBounds(obj, out var min, out var max, out var id))
                        result.Add(new CollisionItem(id, min, max, TextKind.MultiCadLeader));
                }
            }
            catch { /* MultiCAD not available */ }

            return result;
        }

        private static bool TryGetMultiCadLeaderBounds(object obj, out Point3d min, out Point3d max, out ObjectId id)
        {
            min = Point3d.Origin;
            max = Point3d.Origin;
            id = ObjectId.Null;

            // Get ObjectId
            var idProp = obj.GetType().GetProperty("ObjectId")?.GetValue(obj);
            if (idProp != null) id = (ObjectId)idProp;

            // Get text position from known properties
            foreach (var propName in AnchorPointProperties)
            {
                var prop = obj.GetType().GetProperty(propName);
                if (prop?.CanRead == true && TryGetPoint(prop.GetValue(obj), out var pt))
                {
                    min = new Point3d(pt.X - 5, pt.Y - 2, 0);
                    max = new Point3d(pt.X + 25, pt.Y + 5, 0);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetPoint(object? value, out Point3d point)
        {
            point = Point3d.Origin;
            if (value == null) return false;

            var t = value.GetType();
            if (t.Name == "Point3d") { point = (Point3d)value; return true; }
            if (t.Name == "Point3")
            {
                var x = (double)t.GetProperty("X").GetValue(value);
                var y = (double)t.GetProperty("Y").GetValue(value);
                var z = (double)t.GetProperty("Z").GetValue(value);
                point = new Point3d(x, y, z);
                return true;
            }
            return false;
        }

        private static bool IsLeaderCandidate(object obj)
        {
            var t = obj.GetType();
            var typeName = t.Name;
            var fullName = t.FullName ?? "";
            var rxName = t.GetCustomAttributes(false).Select(a => a.ToString() ?? "");
            return LeaderTypeMarkers.Any(marker =>
                typeName.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static double CalculateGap(CollisionItem a, CollisionItem b)
        {
            var dx = Math.Max(0, Math.Max(a.MinX, b.MinX) - Math.Min(a.MaxX, b.MaxX));
            var dy = Math.Max(0, Math.Max(a.MinY, b.MinY) - Math.Min(a.MaxY, b.MaxY));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private enum TextKind { DbText, MText, MLeader, MultiCadLeader }

        private sealed class CollisionItem
        {
            public ObjectId Id { get; }
            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public TextKind Kind { get; }
            public double CenterX => (MinX + MaxX) / 2;
            public double CenterY => (MinY + MaxY) / 2;
            public double Width => MaxX - MinX;
            public double Height => MaxY - MinY;

            public CollisionItem(ObjectId id, Point3d min, Point3d max, TextKind kind)
            {
                Id = id;
                MinX = min.X; MinY = min.Y;
                MaxX = max.X; MaxY = max.Y;
                Kind = kind;
            }
        }
    }

    public sealed class CollisionCleanupResult
    {
        public int TotalTexts { get; set; }
        public int CollisionsFound { get; set; }
        public int ResolvedCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
