using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kayla.NET.Models;
using UtfUnknown;

namespace Kayla.NET.Parsers
{
    public class SAMIParser : ISubtitleParser
    {
        public string FileExtension { get; set; } = ".smi";

        // 시간 정보를 추출하여 정수형으로 반환하는 함수입니다.
        static int GetTime(string line)
        {
            string pattern = @"Start=(\d+)";
            Match match = Regex.Match(line, pattern);
            if (match.Success)
            {
                return Int32.Parse(match.Groups[1].Value);
            }
            else
            {
                return 0;
            }
        }

        public bool ParseFormat(string path, out List<SubtitleItem> result)
        {
            // 입력 파일을 줄 단위로 읽어들입니다.
            List<string> lines = File.ReadAllLines(path).ToList();

            // 각 라인에서 <Sync> 태그를 대문자로 변경합니다.
            List<string> modifiedLines = new List<string>();
            foreach (string line123 in lines)
            {
                string stringB = line123;
                // <P Class=KRCC> 누락 자막도 많아서 대응
                if (line123.StartsWith("<SYNC") && !line123.Contains("<P Class=KRCC>"))
                {
                    stringB = line123.Insert(line123.IndexOf(">") + 1, "<P Class=KRCC>");
                }

                // <Font Face="폰트명" 태그가 있지만 닫히지 않았다면 > 추가
                Match match = Regex.Match(stringB, "<Font Face=\"(.*?)\"(?=[^>]*$)");
                if (match.Success && !match.Groups[0].Value.EndsWith(">"))
                {
                    stringB = stringB.Replace(match.Groups[1].Value, match.Groups[1].Value + ">");
                }

                // sync 대소문자 변경
                string modifiedLine = Regex.Replace(stringB, @"(?i)<sync", "<SYNC");
                // <P Class=KRCC> 이후 줄 바꿈 안하면 변환 안됨... 그래서 줄바꿈 추가
                modifiedLines.Add(Regex.Replace(modifiedLine, @"(<P.*?>)(.+)", "$1\r\n$2"));
            }

            // 시간 정보가 포함된 라인만 선택하여 리스트에 저장합니다.
            List<string> timeLines = modifiedLines.Where(x => x.StartsWith("<SYNC")).ToList();

            // 시간 정보를 기준으로 오름차순으로 정렬합니다.
            List<string> sortedLines = timeLines.OrderBy(x => GetTime(x)).ToList();

            // 정렬된 내용을 새로운 리스트에 저장합니다.
            List<string> outputLines = new List<string>();
            int currentIndex = 0;
            foreach (string line234 in modifiedLines)
            {
                if (line234.StartsWith("<SYNC"))
                {
                    // 시간 정보가 포함된 라인이면, 정렬된 리스트에서 라인을 가져옵니다.
                    outputLines.Add(sortedLines[currentIndex]);
                    currentIndex++;
                }
                else
                {
                    // 그 외의 라인은 그대로 출력합니다.
                    outputLines.Add(line234);
                }
            }

            // 변경된 내용과 정렬된 내용을 새로운 파일에 저장합니다.
            File.WriteAllLines(path, outputLines);

            // 기존 코드 시작
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var detect = CharsetDetector.DetectFromFile(path);
            var encoding = Encoding.GetEncoding(detect.Detected.EncodingName);

            var items = new List<SubtitleItem>();
            var sr = new StreamReader(path, encoding);

            var line = sr.ReadLine();
            if (line == null || !line.Equals("<SAMI>"))
            {
                sr.Close();
                result = null;
                return false;
            }

            while ((line = sr.ReadLine()) != null)
            {
                if (line.Equals("<BODY>"))
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(line))
            {
                sr.Close();
                result = null;
                return false;
            }

            var check = false;
            var miClassString = new string[2];
            var sb = new StringBuilder();
            var sbComment = false;

            while (string.IsNullOrEmpty(line) != true)
            {
                if (check == false)
                {
                    line = sr.ReadLine();

                    while (true)
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            line = sr.ReadLine();
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    check = false;
                }

                if (line.Contains("<--") && line.Contains("-->"))
                {
                    continue;
                }

                if (line.Contains("<!--") && line.Contains("-->"))
                {
                    continue;
                }

                if (line.Contains("<!--"))
                {
                    sbComment = true;
                }

                if (line.Contains("-->"))
                {
                    sbComment = false;
                }

                if (sbComment)
                {
                    continue;
                }

                if (line.Contains("</BODY>"))
                {
                    break;
                }

                if (line.Contains("</SAMI>"))
                {
                    break;
                }

                if (line[0].Equals('<'))
                {
                    var length = line.IndexOf('>');
                    miClassString[0] = line.Substring(1, length - 1);
                    miClassString[1] = line.Substring(length + 2);
                    var splitIndex = miClassString[1].IndexOf('>');
                    miClassString[1] = miClassString[1].Remove(splitIndex);
                    var miSync = miClassString[0].Split('=');

                    while ((line = sr.ReadLine())?.ToUpper().Contains("<SYNC", StringComparison.OrdinalIgnoreCase) ==
                           false)
                    {
                        sb.Append(line);
                    }

                    items.Add(new SubtitleItem(int.Parse(miSync[1]), ConvertString(sb.ToString())));

                    sb = new StringBuilder();

                    check = true;
                }
            }

            sr.Close();

            for (var i = 0; i < items.Count; i++)
            {
                var endTime = i == items.Count - 1
                    ? items[i].StartTime + 1000
                    : items[i + 1].StartTime;

                items[i].EndTime = endTime;
            }

            result = Filters.RemoveDuplicateItems(items);
            return true;
        }

        private string ConvertString(string str)
        {
            str = str.Replace("<br>", "\n");
            str = str.Replace("<BR>", "\n");
            str = str.Replace("&nbsp;", "");
            str = str.Replace("<--", "");
            str = str.Replace("<!--", "");
            str = str.Replace("-->", "");

            try
            {
                while (str.IndexOf("<", StringComparison.Ordinal) != -1)
                {
                    var i = str.IndexOf("<", StringComparison.Ordinal);
                    var j = str.IndexOf(">", StringComparison.Ordinal);
                    str = str.Remove(i, j - i + 1);
                }

                return str;
            }
            catch
            {
                return str;
            }
        }
    }
}