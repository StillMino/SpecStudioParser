using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SpecStudioParser.Models;

namespace SpecStudioParser.Services
{
    /// <summary>
    /// Pure specification generation engine.
    /// Encapsulates filtering, formula evaluation, grouping, and aggregation logic.
    /// Free of UI, Dispatcher, and nanoCAD/Teigha dependencies.
    /// </summary>
    public sealed class SpecificationEngine : ISpecificationEngine
    {
        /// <summary>
        /// Aggregation mode: no aggregation (column is a grouping key).
        /// </summary>
        private const int AggregateNone = 0;

        /// <summary>
        /// Aggregation mode: count rows in each group.
        /// </summary>
        private const int AggregateCount = 1;

        /// <summary>
        /// Aggregation mode: sum numeric values in each group.
        /// </summary>
        private const int AggregateSum = 8;

        /// <inheritdoc/>
        public SpecificationResult Generate(ReportProfile profile, IReadOnlyList<SpecSourceObject> sourceObjects)
        {
            if (profile == null || sourceObjects == null || sourceObjects.Count == 0 || profile.Datasets.Count == 0)
            {
                return new SpecificationResult();
            }

            var aggregatedReportList = new List<IReadOnlyDictionary<string, object>>();

            foreach (var dataset in profile.Datasets)
            {
                var datasetRows = ProcessDataset(dataset, sourceObjects);
                aggregatedReportList.AddRange(datasetRows);
            }

            return new SpecificationResult
            {
                Rows = aggregatedReportList
            };
        }

        /// <summary>
        /// Processes a single dataset: filters objects by type, evaluates filter conditions
        /// and column formulas, then groups and aggregates the result.
        /// </summary>
        private static List<IReadOnlyDictionary<string, object>> ProcessDataset(
            DatasetConfig dataset,
            IReadOnlyList<SpecSourceObject> sourceObjects)
        {
            var stepEvaluatedRows = new List<Dictionary<string, string>>();

            foreach (var dwgObj in sourceObjects)
            {
                if (dataset.TargetTypes.Count > 0 &&
                    !dataset.TargetTypes.Contains(dwgObj.ObjectName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var evalDict = BuildEvaluationContext(dwgObj);

                if (!FilterConditionEvaluator.Matches(dataset, evalDict))
                {
                    continue;
                }

                var row = EvaluateRow(dataset, evalDict, dwgObj.Handle, dwgObj.RawObjectIdString);
                stepEvaluatedRows.Add(row);
            }

            return GroupAndAggregate(dataset, stepEvaluatedRows);
        }

        /// <summary>
        /// Builds the evaluation context dictionary from a source object's attributes
        /// and its core properties (Handle, ObjectName, Layer).
        /// </summary>
        private static Dictionary<string, string> BuildEvaluationContext(SpecSourceObject obj)
        {
            var evalDict = new Dictionary<string, string>(obj.Attributes, StringComparer.OrdinalIgnoreCase);
            evalDict["Handle"] = obj.Handle;
            evalDict["ObjectName"] = obj.ObjectName;
            evalDict["Layer"] = obj.Layer;
            return evalDict;
        }

        /// <summary>
        /// Evaluates all column formulas for a single object and produces a row dictionary.
        /// </summary>
        private static Dictionary<string, string> EvaluateRow(
            DatasetConfig dataset,
            Dictionary<string, string> context,
            string handle,
            string rawObjectIdString)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in dataset.Columns)
            {
                row[col.Caption] = FormulaEvaluator.Evaluate(col.DataFormula, context);
            }
            row["__Handle"] = handle;
            row["__RawObjectIdString"] = rawObjectIdString;
            return row;
        }

        /// <summary>
        /// Groups evaluated rows by visible non-aggregate columns and applies
        /// count/sum aggregation to aggregate columns.
        /// </summary>
        private static List<IReadOnlyDictionary<string, object>> GroupAndAggregate(
            DatasetConfig dataset,
            List<Dictionary<string, string>> evaluatedRows)
        {
            var result = new List<IReadOnlyDictionary<string, object>>();

            var groupColumns = dataset.Columns
                .Where(c => c.Aggregate == AggregateNone && c.Visible == 1)
                .ToList();

            var grouped = evaluatedRows.GroupBy(r =>
                string.Join("|", groupColumns.Select(c => r.ContainsKey(c.Caption) ? r[c.Caption] : "")));

            foreach (var g in grouped)
            {
                var repRow = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var first = g.First();

                foreach (var col in groupColumns)
                {
                    repRow[col.Caption] = first[col.Caption];
                }

                foreach (var col in dataset.Columns.Where(c => c.Aggregate != AggregateNone))
                {
                    if (col.Aggregate == AggregateCount)
                    {
                        repRow[col.Caption] = g.Count().ToString();
                    }
                    else if (col.Aggregate == AggregateSum)
                    {
                        repRow[col.Caption] = g.Sum(r =>
                            double.TryParse(
                                r.ContainsKey(col.Caption) ? r[col.Caption] : "0",
                                NumberStyles.Any,
                                CultureInfo.InvariantCulture,
                                out double d) ? d : 0).ToString();
                    }
                }

                repRow["__Handle"] = first["__Handle"];
                repRow["__RawObjectIdString"] = first["__RawObjectIdString"];
                result.Add(repRow);
            }

            return result;
        }
    }
}
