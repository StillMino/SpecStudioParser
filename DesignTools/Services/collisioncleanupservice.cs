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
            "Callout"
        };

        private static readonly string[] AnchorPointProperties =
        {
            "TextLocation", "BlockPosition", "Origin", "TextPosition", "TextPos", "PntText",
            "Location", "Position"
        };

        public CollisionCleanupResult DetectAndResolveCollisions(double minDistance, string scope = "text")
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var db = doc?.Database;
            var editor = doc?.Editor;
            if (db == null || editor == null)
                return new CollisionCleanupResult { Message = "Нет активного документа." };

            var items = new List<CollisionItem>();

            // Phase 1: collect from Teigha database
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var className = id.ObjectClass.Name;
                    var includeText = scope is "text" or "all";
                    var includeLeaders = scope is "leaders" or "all";

                    if (includeText && className == "AcDbText")
                    {
                        var txt = (DBText)tx.GetObject(id, OpenMode.ForRead);
                        var ext = txt.GeometricExtents;
                        items.Add(new CollisionItem(id, ext.MinPoint, ext.MaxPoint, TextKind.DbText, null));
                    }
                    else if (includeText && className == "AcDbMText")
                    {
                        var mtxt = (MText)tx.GetObject(id, OpenMode.ForRead);
                        var ext = mtxt.GeometricExtents;
                        items.Add(new CollisionItem(id, ext.MinPoint, ext.MaxPoint, TextKind.MText, null));
                    }
                    else if (includeLeaders && className == "AcDbMLeader")
                    {
                        var ml = (MLeader)tx.GetObject(id, OpenMode.ForRead);
                        if (TryGetMLeaderBounds(ml, out var min, out var max))
                            items.Add(new CollisionItem(id, min, max, TextKind.MLeader, null));
                    }
                }

                tx.Commit();
            }

            // Phase 2: collect MultiCAD leaders from selection
            if (scope is "leaders" or "all")
            {
                var mcState = DesignToolsCommandStateService.GetPendingState(DesignToolsToolKind.CollisionCleanup);
                var collectedMcItems = TryCollectMultiCadLeaderItems(mcState?.MultiCadSelectionIds);
                items.AddRange(collectedMcItems);
            }

            if (items.Count < 2)
                return new CollisionCleanupResult { Message = $"Недостаточно объектов: {items.Count}.", TotalTexts = items.Count };

            // Detect collisions
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
                    Message = $"Коллизий не найдено. Объектов: {items.Count}, порог {minDistance:F1}."
                };

            // Union-Find clustering
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

            // Resolve: Teigha objects via Teigha transaction, MultiCAD via McObject API
            var resolved = 0;
            var teighaItems = new List<(CollisionItem item, double delta, bool horizontal)>();
            var mcItems = new List<(CollisionItem item, double delta, bool horizontal)>();

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

                    if (item.Kind == TextKind.MultiCadLeader)
                        mcItems.Add((item, delta, horizontal));
                    else
                        teighaItems.Add((item, delta, horizontal));
                }
            }

            // Apply Teigha shifts
            if (teighaItems.Count > 0)
            {
                using var tx = db.TransactionManager.StartTransaction();
                foreach (var (item, delta, horizontal) in teighaItems)
                {
                    if (ApplyTeighaShift(tx, item, delta, horizontal))
                        resolved++;
                }
                tx.Commit();
            }

            // Apply MultiCAD shifts
            if (mcItems.Count > 0)
            {
                resolved += ApplyMultiCadShifts(mcItems);
            }

            return new CollisionCleanupResult
            {
                TotalTexts = items.Count,
                CollisionsFound = collisionPairs.Count,
                ResolvedCount = resolved,
                Message = $"Кластеров: {collisionRoots.Count}, пар: {collisionPairs.Count}, сдвинуто: {resolved}, порог: {minDistance:F1}."
            };
        }

        private static bool TryGetMLeaderBounds(MLeader ml, out Point3d min, out Point3d max)
        {
            try
            {
                var ext = ml.GeometricExtents;
                min = ext.MinPoint;
                max = ext.MaxPoint;
                return true;
            }
            catch
            {
                // Fallback: use TextLocation with reasonable padding
                try
                {
                    var tl = ml.TextLocation;
                    min = new Point3d(tl.X - 15, tl.Y - 5, 0);
                    max = new Point3d(tl.X + 15, tl.Y + 5, 0);
                    return true;
                }
                catch
                {
                    min = Point3d.Origin;
                    max = Point3d.Origin;
                    return false;
                }
            }
        }

        private static bool IsHorizontalSpread(List<CollisionItem> items, List<int> members)
        {
            var spreadX = members.Max(i => items[i].CenterX) - members.Min(i => items[i].CenterX);
            var spreadY = members.Max(i => items[i].CenterY) - members.Min(i => items[i].CenterY);
            return spreadX >= spreadY;
        }

        private static bool ApplyTeighaShift(Transaction tx, CollisionItem item, double delta, bool horizontal)
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
                default:
                    return false;
            }
        }

        #region MultiCAD leader handling

        private int ApplyMultiCadShifts(List<(CollisionItem item, double delta, bool horizontal)> shifts)
        {
            var resolved = 0;
            try
            {
                var mcAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Multicad");
                if (mcAssembly == null) return 0;

                var objectManagerType = mcAssembly.GetType("Multicad.ObjectManager");
                if (objectManagerType == null) return 0;

                // Start McTransaction
                InvokeTransactionMethod(objectManagerType, "Start");

                try
                {
                    foreach (var (item, delta, horizontal) in shifts)
                    {
                        if (item.McObject != null && TryShiftMultiCadLeader(item.McObject, delta, horizontal))
                            resolved++;
                    }

                    InvokeTransactionMethod(objectManagerType, "End");
                    InvokeTransactionMethod(objectManagerType, "UpdateGraphics");
                    objectManagerType.GetMethod("UpdateAll", BindingFlags.Static | BindingFlags.Public, Type.EmptyTypes)
                        ?.Invoke(null, Array.Empty<object>());
                }
                catch
                {
                    InvokeTransactionMethod(objectManagerType, "Abort");
                    throw;
                }
            }
            catch { /* MultiCAD not available */ }

            return resolved;
        }

        private static bool TryShiftMultiCadLeader(object obj, double delta, bool horizontal)
        {
            // Try property-based shift
            foreach (var propName in AnchorPointProperties)
            {
                var prop = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (prop?.CanRead == true && prop.CanWrite && TryGetPoint(prop.GetValue(obj), out var pt))
                {
                    var newPt = horizontal
                        ? CreateMcPoint(prop.PropertyType, pt.X + delta, pt.Y, pt.Z)
                        : CreateMcPoint(prop.PropertyType, pt.X, pt.Y + delta, pt.Z);
                    if (newPt != null)
                    {
                        prop.SetValue(obj, newPt);
                        return true;
                    }
                }
            }

            // Try Get/Set method pairs
            foreach (var baseName in new[] { "TextLocation", "BlockPosition", "ContentLocation", "DoglegPoint" })
            {
                var getMethod = obj.GetType().GetMethod("Get" + baseName, BindingFlags.Instance | BindingFlags.Public);
                var setMethod = obj.GetType().GetMethod("Set" + baseName, BindingFlags.Instance | BindingFlags.Public);
                if (getMethod != null && setMethod != null)
                {
                    var current = getMethod.Invoke(obj, null);
                    if (TryGetPoint(current, out var pt))
                    {
                        var newPt = horizontal
                            ? CreateMcPoint(getMethod.ReturnType, pt.X + delta, pt.Y, pt.Z)
                            : CreateMcPoint(getMethod.ReturnType, pt.X, pt.Y + delta, pt.Z);
                        if (newPt != null)
                        {
                            setMethod.Invoke(obj, new[] { newPt });
                            return true;
                        }
                    }
                }
            }

            // Try TransformBy with translation matrix (most reliable for McNote)
            return TryTransformByShift(obj, delta, horizontal);
        }

        private static bool TryTransformByShift(object obj, double delta, bool horizontal)
        {
            try
            {
                var transformMethod = obj.GetType().GetMethod("TransformBy", BindingFlags.Instance | BindingFlags.Public);
                if (transformMethod == null) return false;

                var matrixParamType = transformMethod.GetParameters()[0].ParameterType;

                // Build translation matrix: create identity then set translation
                var identity = Activator.CreateInstance(matrixParamType);
                var setMethod = matrixParamType.GetMethod("SetToTranslation", BindingFlags.Instance | BindingFlags.Public);
                if (setMethod != null)
                {
                    // Create translation vector
                    var vecType = matrixParamType.Assembly.GetType(
                        matrixParamType.FullName?.Contains("Matrix3d") == true ? "Teigha.Geometry.Vector3d" :
                        matrixParamType.Namespace + ".Vector3");
                    if (vecType != null)
                    {
                        var tx = horizontal ? delta : 0;
                        var ty = horizontal ? 0 : delta;
                        var vec = Activator.CreateInstance(vecType, tx, ty, 0.0);
                        setMethod.Invoke(identity, new[] { vec });
                        transformMethod.Invoke(obj, new[] { identity });
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static object? CreateMcPoint(Type pointType, double x, double y, double z)
        {
            try
            {
                if (pointType.Name == "Point3d")
                    return Activator.CreateInstance(pointType, x, y, z);
                if (pointType.Name == "Point3")
                {
                    var ctors = pointType.GetConstructors();
                    var ctor3 = ctors.FirstOrDefault(c => c.GetParameters().Length == 3
                        && c.GetParameters().All(p => p.ParameterType == typeof(double)));
                    if (ctor3 != null)
                        return ctor3.Invoke(new object[] { x, y, z });
                }
                return null;
            }
            catch { return null; }
        }

        private static List<CollisionItem> TryCollectMultiCadLeaderItems(List<object>? cachedSelectionIds)
        {
            var result = new List<CollisionItem>();
            try
            {
                var mcAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Multicad");
                if (mcAssembly == null) return result;

                var objectManagerType = mcAssembly.GetType("Multicad.ObjectManager");
                if (objectManagerType == null) return result;

                // Get selection IDs: cached or live
                List<object> selectionIds;
                if (cachedSelectionIds != null && cachedSelectionIds.Count > 0)
                    selectionIds = cachedSelectionIds;
                else
                    selectionIds = GetCurrentMultiCadSelection(objectManagerType);

                if (selectionIds.Count == 0) return result;

                foreach (var id in selectionIds)
                {
                    var obj = GetMultiCadObject(objectManagerType, id);
                    if (obj == null || !IsMcLeaderCandidate(obj)) continue;

                    if (TryGetMcLeaderBounds(obj, out var min, out var max))
                        result.Add(new CollisionItem(default, min, max, TextKind.MultiCadLeader, obj));
                }
            }
            catch { /* MultiCAD not available */ }

            return result;
        }

        private static bool TryGetMcLeaderBounds(object obj)
        {
            // Try GeometricExtents first
            try
            {
                var extProp = obj.GetType().GetProperty("GeometricExtents");
                if (extProp != null)
                {
                    var ext = extProp.GetValue(obj);
                    if (ext != null)
                    {
                        var minProp = ext.GetType().GetProperty("MinPoint");
                        var maxProp = ext.GetType().GetProperty("MaxPoint");
                        if (minProp != null && maxProp != null)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryGetMcLeaderBounds(object obj, out Point3d min, out Point3d max)
        {
            min = Point3d.Origin;
            max = Point3d.Origin;

            // Try GeometricExtents
            try
            {
                var extProp = obj.GetType().GetProperty("GeometricExtents");
                if (extProp != null)
                {
                    var ext = extProp.GetValue(obj);
                    if (ext != null)
                    {
                        var minPt = ext.GetType().GetProperty("MinPoint")?.GetValue(ext);
                        var maxPt = ext.GetType().GetProperty("MaxPoint")?.GetValue(ext);
                        if (minPt != null && maxPt != null && TryGetPoint(minPt, out var minP) && TryGetPoint(maxPt, out var maxP))
                        {
                            min = minP;
                            max = maxP;
                            return true;
                        }
                    }
                }
            }
            catch { }

            // Try GeometryCache for real bbox
            try
            {
                var cacheProp = obj.GetType().GetProperty("GeometryCache");
                if (cacheProp != null)
                {
                    var cache = cacheProp.GetValue(obj);
                    if (cache is System.Collections.IList list && list.Count > 0)
                    {
                        var allX = new List<double>();
                        var allY = new List<double>();
                        foreach (var geom in list)
                        {
                            var geomType = geom.GetType();
                            // Try StartPoint/EndPoint
                            var sp = geomType.GetProperty("StartPoint")?.GetValue(geom);
                            var ep = geomType.GetProperty("EndPoint")?.GetValue(geom);
                            if (sp != null && TryGetPoint(sp, out var p))
                            {
                                allX.Add(p.X); allY.Add(p.Y);
                            }
                            if (ep != null && TryGetPoint(ep, out var p2))
                            {
                                allX.Add(p2.X); allY.Add(p2.Y);
                            }
                        }
                        if (allX.Count > 0)
                        {
                            min = new Point3d(allX.Min(), allY.Min(), 0);
                            max = new Point3d(allX.Max(), allY.Max(), 0);
                            return true;
                        }
                    }
                }
            }
            catch { }

            // Fallback: anchor point with padding
            foreach (var propName in AnchorPointProperties)
            {
                var prop = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (prop?.CanRead == true && TryGetPoint(prop.GetValue(obj), out var pt))
                {
                    min = new Point3d(pt.X - 15, pt.Y - 5, 0);
                    max = new Point3d(pt.X + 25, pt.Y + 8, 0);
                    return true;
                }
            }

            return false;
        }

        private static List<object> GetCurrentMultiCadSelection(Type objectManagerType)
        {
            var result = new List<object>();
            var selectionSet = objectManagerType.GetProperty("SelectionSet", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            object? currentSelection = null;
            if (selectionSet != null)
            {
                currentSelection = selectionSet.GetType().GetProperty("CurrentSelection", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)?.GetValue(selectionSet);
            }
            if (currentSelection is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    if (item != null) result.Add(item);
            }
            return result;
        }

        private static object? GetMultiCadObject(Type objectManagerType, object id)
        {
            var getObjectMethod = objectManagerType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetObject" && m.GetParameters().Length == 1);
            if (getObjectMethod != null) return getObjectMethod.Invoke(null, new[] { id });
            return id.GetType().GetMethod("GetObject", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
                ?.Invoke(id, Array.Empty<object>());
        }

        private static bool IsMcLeaderCandidate(object obj)
        {
            var type = obj.GetType();
            var typeName = type.Name;
            var fullName = type.FullName ?? "";
            return LeaderTypeMarkers.Any(marker =>
                typeName.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static void InvokeTransactionMethod(Type objectManagerType, string methodName)
        {
            var transactions = objectManagerType.GetProperty("Transactions", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            transactions?.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public, Type.EmptyTypes)
                ?.Invoke(transactions, Array.Empty<object>());
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
                var zProp = t.GetProperty("Z");
                var z = zProp != null ? (double)zProp.GetValue(value) : 0;
                point = new Point3d(x, y, z);
                return true;
            }
            return false;
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
            public object? McObject { get; }
            public double CenterX => (MinX + MaxX) / 2;
            public double CenterY => (MinY + MaxY) / 2;
            public double Width => MaxX - MinX;
            public double Height => MaxY - MinY;

            public CollisionItem(ObjectId id, Point3d min, Point3d max, TextKind kind, object? mcObject)
            {
                Id = id; MinX = min.X; MinY = min.Y; MaxX = max.X; MaxY = max.Y;
                Kind = kind; McObject = mcObject;
            }
        }

        #endregion
    }

    public sealed class CollisionCleanupResult
    {
        public int TotalTexts { get; set; }
        public int CollisionsFound { get; set; }
        public int ResolvedCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
