using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SpecStudioParser.Services
{
    public static class FormulaEvaluator
    {
        /// <summary>
        /// Вычисляет формулу или многоуровневое текстовое условие на основе атрибутов объекта.
        /// Поддерживает формат: if([Атрибут1] != '', [Атрибут1], [Атрибут2])
        /// </summary>
        public static string Evaluate(string formula, Dictionary<string, string> attributes)
        {
            if (string.IsNullOrWhiteSpace(formula)) return string.Empty;

            string processed = formula.Trim();

            try
            {
                // Рекурсивно обрабатываем операторы условий if(...) начиная с самых глубоких вложений
                while (processed.StartsWith("if(", StringComparison.OrdinalIgnoreCase))
                {
                    int firstOpenBracket = processed.IndexOf('(');
                    int matchingCloseBracket = FindMatchingClosingBracket(processed, firstOpenBracket);

                    if (matchingCloseBracket == -1) break;

                    // Вытаскиваем внутренности текущего if
                    string body = processed.Substring(firstOpenBracket + 1, matchingCloseBracket - firstOpenBracket - 1);

                    // Разделяем аргументы функции if (Условие, Правда, Ложь) с учетом запятых внутри подфункций
                    List<string> args = SplitFormulaArguments(body);
                    if (args.Count < 3) break;

                    string condition = ResolveTokens(args[0], attributes);
                    bool isTrue = ExecuteLogicalCondition(condition);

                    // Выбираем нужную ветку выполнения
                    string selectedBranch = isTrue ? args[1] : args[2];

                    // Подставляем результат вычисления вместо текущего блока if(...)
                    processed = processed.Substring(0, firstOpenBracket - 2) + selectedBranch + processed.Substring(matchingCloseBracket + 1);
                    processed = processed.Trim();
                }

                // В конце подставляем оставшиеся одиночные переменные-токены типа [Part_Name]
                return ResolveTokens(processed, attributes).Replace("\"", "").Replace("'", "").Trim();
            }
            catch (Exception ex)
            {
                return $"[Ошибка формулы: {ex.Message}]";
            }
        }

        private static string ResolveTokens(string expression, Dictionary<string, string> attributes)
        {
            // Находит все вхождения вида [ИМЯ_АТРИБУТА]
            return Regex.Replace(expression, @"\[(.*?)\]", m =>
            {
                string key = m.Groups[1].Value;
                if (attributes.TryGetValue(key, out string? val))
                {
                    return $"\"{val}\"";
                }
                return "\"\""; // Если атрибута нет, возвращаем пустую строку
            });
        }

        private static bool ExecuteLogicalCondition(string condition)
        {
            // Очищаем и нормализуем логическое выражение
            string cond = condition.Replace("\"", "").Replace("'", "").Trim();

            if (cond.Contains("!="))
            {
                string[] parts = cond.Split(new[] { "!=" }, StringSplitOptions.None);
                return parts[0].Trim() != parts[1].Trim();
            }
            if (cond.Contains("=="))
            {
                string[] parts = cond.Split(new[] { "==" }, StringSplitOptions.None);
                return parts[0].Trim() == parts[1].Trim();
            }
            if (cond.Contains("="))
            {
                string[] parts = cond.Split(new[] { "=" }, StringSplitOptions.None);
                return parts[0].Trim() == parts[1].Trim();
            }

            // Базовая проверка на заполненность, если передан просто токен
            return !string.IsNullOrEmpty(cond);
        }

        private static int FindMatchingClosingBracket(string text, int openBracketIndex)
        {
            int counter = 0;
            for (int i = openBracketIndex; i < text.Length; i++)
            {
                if (text[i] == '(') counter++;
                if (text[i] == ')') counter--;
                if (counter == 0) return i;
            }
            return -1;
        }

        private static List<string> SplitFormulaArguments(string body)
        {
            var args = new List<string>();
            int bracketCounter = 0;
            int startIdx = 0;

            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] == '(') bracketCounter++;
                if (body[i] == ')') bracketCounter--;

                // Делим по запятой только на верхнем уровне вложенности функций
                if (body[i] == ',' && bracketCounter == 0)
                {
                    args.Add(body.Substring(startIdx, i - startIdx).Trim());
                    startIdx = i + 1;
                }
            }
            args.Add(body.Substring(startIdx).Trim());
            return args;
        }
    }
}