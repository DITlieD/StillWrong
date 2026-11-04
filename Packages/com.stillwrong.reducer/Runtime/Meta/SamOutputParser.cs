using System;
using System.Collections.Generic;

namespace StillWrong.Meta
{
    public enum SamMaskEncoding
    {
        OneBit,
        Lossless
    }

    public sealed class SamBoxRecord
    {
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public double? Confidence { get; set; }
    }

    public sealed class SamMaskRecord
    {
        public int Height { get; set; }
        public int Width { get; set; }
        public SamMaskEncoding Encoding { get; set; }
        public string Payload { get; set; }
        public double? Confidence { get; set; }

        public SamMaskRecord()
        {
            Payload = string.Empty;
        }
    }

    public sealed class SamFrameRecord
    {
        public int FrameIndex { get; set; }
        public string ObjectId { get; set; }
        public SamBoxRecord Box { get; set; }
        public SamMaskRecord Mask { get; set; }

        public SamFrameRecord()
        {
            ObjectId = string.Empty;
            Box = new SamBoxRecord();
            Mask = new SamMaskRecord();
        }
    }

    public sealed class SamParseDiagnostic
    {
        public int LineNumber { get; set; }
        public string RawText { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }

        public SamParseDiagnostic()
        {
            RawText = string.Empty;
            Code = string.Empty;
            Message = string.Empty;
        }
    }

    public sealed class SamParseResult
    {
        public List<SamFrameRecord> Records { get; set; }
        public List<SamParseDiagnostic> Diagnostics { get; set; }

        public SamParseResult()
        {
            Records = new List<SamFrameRecord>();
            Diagnostics = new List<SamParseDiagnostic>();
        }
    }

    public static class SamOutputParser
    {
        private const long MaxSafeInteger = 9007199254740991L;

        public static SamParseResult Parse(string outputText)
        {
            SamParseResult result = new SamParseResult();
            if (outputText == null || outputText.Length == 0)
            {
                return result;
            }
            string normalized = outputText.Replace(((char)13).ToString(), string.Empty);
            string[] lines = normalized.Split((char)10);
            int lineNumber = 0;
            for (int li = 0; li < lines.Length; li++)
            {
                string raw = lines[li];
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                lineNumber++;
                List<SamFrameRecord> lineRecords;
                string errorCode;
                string errorMessage;
                if (TryParseLine(line, raw, lineNumber, out lineRecords, out errorCode, out errorMessage))
                {
                    result.Records.AddRange(lineRecords);
                }
                else
                {
                    SamParseDiagnostic diagnostic = new SamParseDiagnostic();
                    diagnostic.LineNumber = lineNumber;
                    diagnostic.RawText = raw;
                    diagnostic.Code = errorCode;
                    diagnostic.Message = errorMessage;
                    result.Diagnostics.Add(diagnostic);
                }
            }
            return result;
        }

        private static bool TryParseLine(string line, string raw, int lineNumber, out List<SamFrameRecord> records, out string errorCode, out string errorMessage)
        {
            records = new List<SamFrameRecord>();
            errorCode = string.Empty;
            errorMessage = string.Empty;
            if (line.Length == 0 || line[0] != '<')
            {
                errorCode = "malformed_line";
                errorMessage = "Line does not start with a frame marker.";
                return false;
            }
            int closeIndex = line.IndexOf('>');
            if (closeIndex < 0)
            {
                errorCode = "malformed_line";
                errorMessage = "Line is missing the frame marker end.";
                return false;
            }
            string header = line.Substring(0, closeIndex + 1);
            if (!TryParseFrameHeader(header, out int frameIndex))
            {
                errorCode = "invalid_frame";
                errorMessage = "Frame marker is invalid.";
                return false;
            }
            string body = line.Substring(closeIndex + 1);
            if (body.Length == 0)
            {
                errorCode = "malformed_record";
                errorMessage = "Line has a frame marker but no records.";
                return false;
            }
            List<string> recordTexts = SplitRecords(body);
            if (recordTexts.Count == 0)
            {
                errorCode = "malformed_record";
                errorMessage = "Line has no parseable records.";
                return false;
            }
            for (int ri = 0; ri < recordTexts.Count; ri++)
            {
                string recordText = recordTexts[ri];
                SamFrameRecord record;
                if (TryParseRecord(recordText, frameIndex, out record))
                {
                    records.Add(record);
                }
                else
                {
                    errorCode = "malformed_record";
                    errorMessage = "One record on the line is malformed.";
                    records.Clear();
                    return false;
                }
            }
            if (records.Count == 0)
            {
                errorCode = "malformed_record";
                errorMessage = "Line has no valid records.";
                return false;
            }
            return true;
        }

        private static bool TryParseFrameHeader(string header, out int frameIndex)
        {
            frameIndex = 0;
            if (header.Length < 4)
            {
                return false;
            }
            if (header[0] != '<' || header[header.Length - 1] != '>')
            {
                return false;
            }
            string inner = header.Substring(1, header.Length - 2);
            int semiIndex = inner.IndexOf(';');
            if (semiIndex >= 0)
            {
                inner = inner.Substring(0, semiIndex);
            }
            inner = inner.Trim();
            if (inner.Length < 2)
            {
                return false;
            }
            if (inner[inner.Length - 1] != 'f')
            {
                return false;
            }
            string number = inner.Substring(0, inner.Length - 1).Trim();
            if (number.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < number.Length; i++)
            {
                if (number[i] < '0' || number[i] > '9')
                {
                    return false;
                }
            }
            if (!long.TryParse(number, out long parsed))
            {
                return false;
            }
            if (parsed < 0 || parsed > MaxSafeInteger)
            {
                return false;
            }
            frameIndex = (int)parsed;
            return true;
        }

        private static List<string> SplitRecords(string body)
        {
            List<string> parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                }
                else if (c == ',' && depth == 0)
                {
                    parts.Add(body.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(body.Substring(start));
            return parts;
        }

        private static bool TryParseRecord(string recordText, int frameIndex, out SamFrameRecord record)
        {
            record = null;
            if (recordText == null || recordText.Length == 0)
            {
                return false;
            }
            string text = recordText.Trim();
            if (text.Length == 0)
            {
                return false;
            }
            int pos = 0;
            while (pos < text.Length && text[pos] >= '0' && text[pos] <= '9')
            {
                pos++;
            }
            if (pos == 0)
            {
                return false;
            }
            string objectId = text.Substring(0, pos);
            string rest = text.Substring(pos);
            List<TokenRead> tokens = ReadTokens(rest);
            if (tokens == null || tokens.Count != 2)
            {
                return false;
            }
            TokenRead boxToken = null;
            TokenRead maskToken = null;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Name.Equals("box", StringComparison.Ordinal))
                {
                    if (boxToken != null)
                    {
                        return false;
                    }
                    boxToken = tokens[i];
                }
                else if (tokens[i].Name.Equals("mask", StringComparison.Ordinal))
                {
                    if (maskToken != null)
                    {
                        return false;
                    }
                    maskToken = tokens[i];
                }
                else
                {
                    return false;
                }
            }
            if (boxToken == null || maskToken == null)
            {
                return false;
            }
            SamBoxRecord box;
            if (!TryParseBox(boxToken.Body, out box))
            {
                return false;
            }
            SamMaskRecord mask;
            if (!TryParseMask(maskToken.Body, out mask))
            {
                return false;
            }
            SamFrameRecord parsed = new SamFrameRecord();
            parsed.FrameIndex = frameIndex;
            parsed.ObjectId = objectId;
            parsed.Box = box;
            parsed.Mask = mask;
            record = parsed;
            return true;
        }

        private sealed class TokenRead
        {
            public string Name = string.Empty;
            public string Body = string.Empty;
        }

        private static List<TokenRead> ReadTokens(string rest)
        {
            List<TokenRead> tokens = new List<TokenRead>();
            int i = 0;
            while (i < rest.Length)
            {
                if (rest[i] != '<')
                {
                    return null;
                }
                if (i + 1 >= rest.Length || rest[i + 1] != '|')
                {
                    return null;
                }
                int end = rest.IndexOf("|>", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }
                string inner = rest.Substring(i + 2, end - (i + 2));
                string name;
                string body;
                int semi = inner.IndexOf(';');
                if (semi < 0)
                {
                    name = inner.Trim();
                    body = string.Empty;
                }
                else
                {
                    name = inner.Substring(0, semi).Trim();
                    body = inner.Substring(semi + 1);
                }
                if (name.Length == 0)
                {
                    return null;
                }
                TokenRead token = new TokenRead();
                token.Name = name;
                token.Body = body;
                tokens.Add(token);
                i = end + 2;
            }
            return tokens;
        }

        private static Dictionary<string, string> ReadFields(string body, out bool duplicateNonConfidence, out bool repeatedConfidence)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            duplicateNonConfidence = false;
            repeatedConfidence = false;
            if (body == null || body.Length == 0)
            {
                return values;
            }
            string[] parts = body.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }
                int eq = part.IndexOf('=');
                string key;
                string value;
                if (eq < 0)
                {
                    key = part.Trim();
                    value = null;
                }
                else
                {
                    key = part.Substring(0, eq).Trim();
                    value = part.Substring(eq + 1).Trim();
                }
                if (key.Length == 0)
                {
                    continue;
                }
                if (values.ContainsKey(key))
                {
                    if (key.Equals("c", StringComparison.Ordinal))
                    {
                        repeatedConfidence = true;
                        continue;
                    }
                    duplicateNonConfidence = true;
                    return null;
                }
                values[key] = value;
            }
            return values;
        }

        private static bool TryParseBox(string body, out SamBoxRecord box)
        {
            box = null;
            bool dup;
            bool rep;
            Dictionary<string, string> fields = ReadFields(body, out dup, out rep);
            if (fields == null || dup)
            {
                return false;
            }
            string[] required = new string[] { "x1", "y1", "x2", "y2", "w", "h" };
            for (int i = 0; i < required.Length; i++)
            {
                if (!fields.ContainsKey(required[i]) || fields[required[i]] == null)
                {
                    return false;
                }
            }
            if (!TryParseSigned(fields["x1"], out int x1)) return false;
            if (!TryParseSigned(fields["y1"], out int y1)) return false;
            if (!TryParseSigned(fields["x2"], out int x2)) return false;
            if (!TryParseSigned(fields["y2"], out int y2)) return false;
            if (!TryParseUnsigned(fields["w"], out int w)) return false;
            if (!TryParseUnsigned(fields["h"], out int h)) return false;
            if (w <= 0 || h <= 0) return false;
            if (x1 < 0 || y1 < 0) return false;
            if (x2 < x1 || y2 < y1) return false;
            if (x2 >= w || y2 >= h) return false;
            SamBoxRecord parsed = new SamBoxRecord();
            parsed.X1 = x1;
            parsed.Y1 = y1;
            parsed.X2 = x2;
            parsed.Y2 = y2;
            parsed.W = w;
            parsed.H = h;
            if (fields.ContainsKey("c") && !rep)
            {
                double? confidence = TryParseConfidence(fields["c"]);
                parsed.Confidence = confidence;
            }
            else
            {
                parsed.Confidence = null;
            }
            box = parsed;
            return true;
        }

        private static bool TryParseMask(string body, out SamMaskRecord mask)
        {
            mask = null;
            bool dup;
            bool rep;
            Dictionary<string, string> fields = ReadFields(body, out dup, out rep);
            if (fields == null || dup)
            {
                return false;
            }
            if (!fields.ContainsKey("x") || fields["x"] == null || !fields["x"].Equals("0", StringComparison.Ordinal))
            {
                return false;
            }
            if (!fields.ContainsKey("y") || fields["y"] == null || !fields["y"].Equals("0", StringComparison.Ordinal))
            {
                return false;
            }
            if (!fields.ContainsKey("data") || fields["data"] == null)
            {
                return false;
            }
            string data = fields["data"].Trim();
            int firstComma = data.IndexOf(',');
            int secondComma = firstComma >= 0 ? data.IndexOf(',', firstComma + 1) : -1;
            if (firstComma < 0 || secondComma < 0)
            {
                return false;
            }
            string hText = data.Substring(0, firstComma).Trim();
            string wText = data.Substring(firstComma + 1, secondComma - firstComma - 1).Trim();
            string encAndPayload = data.Substring(secondComma + 1);
            if (encAndPayload.Length < 2)
            {
                return false;
            }
            char marker = encAndPayload[0];
            if (marker != '!' && marker != '~')
            {
                return false;
            }
            string payload = encAndPayload.Substring(1);
            if (payload.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < payload.Length; i++)
            {
                if (payload[i] == '|')
                {
                    return false;
                }
            }
            if (!TryParseUnsigned(hText, out int h)) return false;
            if (!TryParseUnsigned(wText, out int w)) return false;
            if (h <= 0 || w <= 0) return false;
            SamMaskRecord parsed = new SamMaskRecord();
            parsed.Height = h;
            parsed.Width = w;
            parsed.Encoding = marker == '!' ? SamMaskEncoding.OneBit : SamMaskEncoding.Lossless;
            parsed.Payload = payload;
            if (fields.ContainsKey("c") && !rep)
            {
                parsed.Confidence = TryParseConfidence(fields["c"]);
            }
            else
            {
                parsed.Confidence = null;
            }
            mask = parsed;
            return true;
        }

        private static bool TryParseSigned(string text, out int value)
        {
            value = 0;
            if (text == null || text.Length == 0)
            {
                return false;
            }
            int start = 0;
            if (text[0] == '-')
            {
                if (text.Length == 1)
                {
                    return false;
                }
                start = 1;
            }
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }
            if (!long.TryParse(text, out long parsed))
            {
                return false;
            }
            if (parsed < -MaxSafeInteger || parsed > MaxSafeInteger)
            {
                return false;
            }
            value = (int)parsed;
            return true;
        }

        private static bool TryParseUnsigned(string text, out int value)
        {
            value = 0;
            if (text == null || text.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }
            if (!long.TryParse(text, out long parsed))
            {
                return false;
            }
            if (parsed < 0 || parsed > MaxSafeInteger)
            {
                return false;
            }
            value = (int)parsed;
            return true;
        }

        private static double? TryParseConfidence(string text)
        {
            if (text == null)
            {
                return null;
            }
            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }
            if (!double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                return null;
            }
            if (double.IsNaN(parsed) || double.IsInfinity(parsed))
            {
                return null;
            }
            if (parsed < 0.0 || parsed > 1.0)
            {
                return null;
            }
            return parsed;
        }
    }
}
