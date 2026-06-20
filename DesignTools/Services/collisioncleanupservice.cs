using System;
using System.Collections;
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
        public CollisionCleanupResult DetectAndResolveCollisions(double minDistance)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var db = doc?.Database;
            var editor = doc?.Editor;
            if (db == null || editor == null)
                return new CollisionCleanupResult { Message = "Нет активного документа." };

            // Сбор текстовых объектов (DBText, MText)
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
                        textItems.Add(new TextBounds(id, txt.TextString, ext.MinPoint, ext.MaxPoint, TextBounds.GetTextCenter(txt)));
                    }
                    else if (id.ObjectClass.Name == "AcDbMText")
                    {
                        var mtxt = (MText)tx.GetObject(id, OpenMode.ForRead);
                        var ext = mtxt.GeometricExtents;
                        textItems.Add(new TextBounds(id, mtxt.Contents, ext.MinPoint, ext.MaxPoint, mtxt.Location));
                    }
                }

                tx.Commit();
            }

            if (textItems.Count < 2)
                return new CollisionCleanupResult { Message = $"Недостаточно текстовых объектов: {textItems.Count}. Нужно минимум 2.", TotalTexts = textItems.Count };

            // Поиск коллизий (bbox overlap или расстояние < minDistance)
            var collisions = new List<(TextBounds A, TextBounds B, double overlap)>();
            for (var i = 0; i < textItems.Count; i++)
            {
                for (var j = i + 1; j < textItems.Count; j++)
                {
                    var a = textItems[i];
                    var b = textItems[j];
                    var overlap = CalculateOverlap(a, b);
                    var gap = CalculateGap(a, b);
                    if (overlap > 0 || gap < minDistance)
                    {
                        collisions.Add((a, b, overlap > 0 ? overlap : minDistance - gap));
                    }
                }
            }

            if (collisions.Count == 0)
                return new CollisionCleanupResult
                {
                    TotalTexts = textItems.Count,
                    CollisionsFound = 0,
                    Message = $"Коллизий не найдено. Проверено {textItems.Count} текстов, порог {minDistance:F1}."
                };

            // Авто-разнесение: для каждой пары сдвигаем B от A по кратчайшей оси
            var resolved = 0;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var shifted = new HashSet<ObjectId>();
                var shiftVector = new Dictionary<ObjectId, Vector3d>();

                foreach (var (a, b, overlap) in collisions)
                {
                    // Вектор от A к B
                    var dx = b.Center.X - a.Center.X;
                    var dy = b.Center.Y - a.Center.Y;

                    // Сдвигаем B дальше от A по доминантной оси
                    var shift = Math.Max(overlap, minDistance) + 0.5;
                    Vector3d delta;
                    if (Math.Abs(dx) >= Math.Abs(dy))
                        delta = new Vector3d(dx >= 0 ? shift : -shift, 0, 0);
                    else
                        delta = new Vector3d(0, dy >= 0 ? shift : -shift, 0);

                    if (!shifted.Contains(b.Id))
                    {
                        shiftVector[b.Id] = delta;
                        shifted.Add(b.Id);
                    }
                    else
                    {
                        shiftVector[b.Id] += delta;
                    }
                }

                // Применяем сдвиги
                foreach (var kv in shiftVector)
                {
                    var obj = tx.GetObject(kv.Key, OpenMode.ForWrite);
                    var delta = kv.Value;
                    if (obj is DBText dbText)
                    {
                        var current = dbText.Position;
                        dbText.Position = new Point3d(current.X + delta.X, current.Y + delta.Y, current.Z + delta.Z);
                        resolved++;
                    }
                    else if (obj is MText mText)
                    {
                        var current = mText.Location;
                        mText.Location = new Point3d(current.X + delta.X, current.Y + delta.Y, current.Z + delta.Z);
                        resolved++;
                    }
                }

                tx.Commit();
            }

            return new CollisionCleanupResult
            {
                TotalTexts = textItems.Count,
                CollisionsFound = collisions.Count,
                ResolvedCount = resolved,
                Message = $"Найдено коллизий: {collisions.Count}. Сдвинуто текстов: {resolved}. Порог: {minDistance:F1}."
            };
        }

        private static double CalculateOverlap(TextBounds a, TextBounds b)
        {
            var dx = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
            var dy = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
            if (dx > 0 && dy > 0) return dx * dy;
            return 0;
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
            public string Content { get; }
            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public Point3d Center { get; }

            public TextBounds(ObjectId id, string content, Point3d min, Point3d max, Point3d center)
            {
                Id = id;
                Content = content;
                MinX = min.X;
                MinY = min.Y;
                MaxX = max.X;
                MaxY = max.Y;
                Center = center;
            }

            public static Point3d GetTextCenter(DBText txt)
            {
                var ext = txt.GeometricExtents;
                return new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, 0);
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
