using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    public sealed class CollisionCleanupService
    {
        public CollisionCleanupResult DetectAndResolveCollisions(double minDistance)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var db = doc?.Database;
            if (db == null)
                return new CollisionCleanupResult { Message = "Нет активного документа." };

            // Сбор текстовых объектов
            var textItems = new List<TextBounds>();
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass.Name == "AcDbText")
                    {
                        var txt = (DBText)tx.GetObject(id, OpenMode.ForRead);
                        var ext = txt.GeometricExtents;
                        textItems.Add(new TextBounds(id, ext.MinPoint, ext.MaxPoint));
                    }
                    else if (id.ObjectClass.Name == "AcDbMText")
                    {
                        var mtxt = (MText)tx.GetObject(id, OpenMode.ForRead);
                        var ext = mtxt.GeometricExtents;
                        textItems.Add(new TextBounds(id, ext.MinPoint, ext.MaxPoint));
                    }
                }
                tx.Commit();
            }

            if (textItems.Count < 2)
                return new CollisionCleanupResult { Message = $"Недостаточно текстовых объектов: {textItems.Count}.", TotalTexts = textItems.Count };

            // Поиск коллизий
            var collisionPairs = new List<(int A, int B)>();
            for (var i = 0; i < textItems.Count; i++)
            {
                for (var j = i + 1; j < textItems.Count; j++)
                {
                    if (CalculateGap(textItems[i], textItems[j]) < minDistance)
                        collisionPairs.Add((i, j));
                }
            }

            if (collisionPairs.Count == 0)
                return new CollisionCleanupResult
                {
                    TotalTexts = textItems.Count,
                    CollisionsFound = 0,
                    Message = $"Коллизий не найдено. Проверено {textItems.Count} текстов, порог {minDistance:F1}."
                };

            // Union-Find для кластеризации
            var parent = new int[textItems.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
            void Union(int a, int b) => parent[Find(a)] = Find(b);

            foreach (var (a, b) in collisionPairs)
                Union(a, b);

            // Группировка по кластерам
            var clusters = new Dictionary<int, List<int>>();
            for (var i = 0; i < textItems.Count; i++)
            {
                var root = Find(i);
                if (!clusters.ContainsKey(root))
                    clusters[root] = new List<int>();
                clusters[root].Add(i);
            }

            // Обрабатываем только кластеры с коллизиями (>= 2 элементов и хотя бы одна пара)
            var collisionClusterRoots = new HashSet<int>();
            foreach (var (a, b) in collisionPairs)
            {
                collisionClusterRoots.Add(Find(a));
            }

            var resolved = 0;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                foreach (var root in collisionClusterRoots)
                {
                    var members = clusters[root];
                    if (members.Count < 2) continue;

                    // Определяем доминантную ось разброса
                    var xs = members.Select(i => textItems[i].CenterX).ToList();
                    var ys = members.Select(i => textItems[i].CenterY).ToList();
                    var spreadX = xs.Max() - xs.Min();
                    var spreadY = ys.Max() - ys.Min();
                    var horizontal = spreadX >= spreadY;

                    // Сортируем по позиции на доминантной оси
                    members.Sort((a, b) =>
                        horizontal
                            ? textItems[a].CenterX.CompareTo(textItems[b].CenterX)
                            : textItems[a].CenterY.CompareTo(textItems[b].CenterY));

                    // Целевые позиции: равномерно с шагом >= minDistance
                    // Используем максимальный размер объекта в кластере как базу шага
                    double maxExtent = horizontal
                        ? members.Max(i => textItems[i].MaxX - textItems[i].MinX)
                        : members.Max(i => textItems[i].MaxY - textItems[i].MinY);
                    var step = Math.Max(maxExtent + minDistance, minDistance);

                    // Центр масс кластера сохраняем
                    var totalCount = members.Count;
                    var firstCenter = horizontal ? textItems[members[0]].CenterX : textItems[members[0]].CenterY;
                    var lastCenter = horizontal ? textItems[members[^1]].CenterX : textItems[members[^1]].CenterY;

                    // Если вся группа помещается в текущий разброс — используем его
                    // Иначе — расширяем с шагом step от центра
                    var currentSpread = lastCenter - firstCenter;
                    var neededSpread = step * (totalCount - 1);

                    double startPos;
                    if (neededSpread <= currentSpread)
                    {
                        // Достаточно места — распределяем равномерно в текущих границах
                        startPos = firstCenter;
                        step = currentSpread / (totalCount - 1);
                        // Проверяем что шаг не меньше minDistance
                        if (step < minDistance)
                        {
                            // Расширяем
                            step = minDistance;
                            var mid = (firstCenter + lastCenter) / 2.0;
                            startPos = mid - step * (totalCount - 1) / 2.0;
                        }
                    }
                    else
                    {
                        // Расширяем от центра масс
                        var mid = (firstCenter + lastCenter) / 2.0;
                        startPos = mid - step * (totalCount - 1) / 2.0;
                    }

                    // Применяем сдвиги
                    for (var idx = 0; idx < members.Count; idx++)
                    {
                        var item = textItems[members[idx]];
                        double targetPos = startPos + step * idx;
                        double currentPos = horizontal ? item.CenterX : item.CenterY;
                        var delta = targetPos - currentPos;

                        if (Math.Abs(delta) < 0.001) continue;

                        var obj = tx.GetObject(item.Id, OpenMode.ForWrite);
                        if (obj is DBText dbText)
                        {
                            var p = dbText.Position;
                            if (horizontal)
                                dbText.Position = new Point3d(p.X + delta, p.Y, p.Z);
                            else
                                dbText.Position = new Point3d(p.X, p.Y + delta, p.Z);
                            resolved++;
                        }
                        else if (obj is MText mText)
                        {
                            var p = mText.Location;
                            if (horizontal)
                                mText.Location = new Point3d(p.X + delta, p.Y, p.Z);
                            else
                                mText.Location = new Point3d(p.X, p.Y + delta, p.Z);
                            resolved++;
                        }
                    }
                }

                tx.Commit();
            }

            return new CollisionCleanupResult
            {
                TotalTexts = textItems.Count,
                CollisionsFound = collisionPairs.Count,
                ResolvedCount = resolved,
                Message = $"Кластеров: {collisionClusterRoots.Count}, пар: {collisionPairs.Count}, сдвинуто: {resolved}, порог: {minDistance:F1}."
            };
        }

        private static double CalculateGap(TextBounds a, TextBounds b)
        {
            var dx = Math.Max(0, Math.Max(a.MinX, b.MinX) - Math.Min(a.MaxX, b.MaxX));
            var dy = Math.Max(0, Math.Max(a.MinY, b.MinY) - Math.Min(a.MaxY, b.MaxY));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class TextBounds
        {
            public ObjectId Id { get; }
            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public double CenterX => (MinX + MaxX) / 2;
            public double CenterY => (MinY + MaxY) / 2;

            public TextBounds(ObjectId id, Point3d min, Point3d max)
            {
                Id = id;
                MinX = min.X;
                MinY = min.Y;
                MaxX = max.X;
                MaxY = max.Y;
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
