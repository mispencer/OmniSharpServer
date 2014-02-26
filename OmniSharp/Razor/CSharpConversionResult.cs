using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web.Razor.Generator;
using OmniSharp.Common;

namespace OmniSharp.Razor
{
    public class CSharpConversionResult
    {
        public bool Success { get; set; }
        public IList<Error> Errors { get; set; }

        public String Source { get; set; }
        public String OriginalSource { get; set; }
        public IDictionary<int,GeneratedCodeMapping> Mappings { get; set; }

        public LineColumn? ConvertToNewLocation(int line, int column)
        {
            var inputIndex = this.LineColumnToIndex(this.OriginalSource, line, column);
            foreach(var attemptedOffset in new[] { 0, 1, /*-1,*/ 2, /*-2,*/ 3, /*-3,*/ 4, /*-4,*/ 5, /*-5*/ })
            {
                foreach(var mapping in this.Mappings)
                {
                    if (inputIndex - attemptedOffset >= mapping.Value.StartOffset && inputIndex - attemptedOffset <= mapping.Value.StartOffset + mapping.Value.CodeLength)
                    {
                        //Console.WriteLine("MappingLine: "+mapping.Key);
                        var lineIndex = this.FindIndexForLinePragma(this.Source, mapping.Key);
                        //Console.WriteLine("MappingParts: "+lineIndex+", "+mapping.Value.StartGeneratedColumn+", "+mapping.Value.StartColumn+", "+mapping.Value.StartOffset.Value+", "+inputIndex);
                        //Console.WriteLine("Around: [[[`"+output.Source.Substring(lineIndex-30, 30)+"`"+output.Source.Substring(lineIndex, 30)+"`]]]");
                        var locationIndex = lineIndex + mapping.Value.StartGeneratedColumn + (inputIndex-mapping.Value.StartOffset.Value-1);
                        if (attemptedOffset != 0)
                        {
                            this.Source = this.Source.Substring(0, locationIndex-attemptedOffset)+this.OriginalSource.Substring(inputIndex-attemptedOffset, attemptedOffset)+this.Source.Substring(locationIndex-attemptedOffset);
                            //Console.WriteLine("Source: \n"+this.Source);
                        }
                        //Console.WriteLine("Around: [[[`"+this.Source.Substring(locationIndex-30, 30)+"`"+this.Source.Substring(locationIndex, 30)+"`]]]");
                        return this.IndexToLineColumn(this.Source, locationIndex);
                    }
                }
            }
            //Console.WriteLine("Couldn't find location: "+inputIndex);
            //foreach(var mapping in this.Mappings)
            //{
            //    Console.WriteLine(String.Format("Mapping: {0} - {1} ({2})", mapping.Value.StartOffset, mapping.Value.StartOffset + mapping.Value.CodeLength, mapping.Value.CodeLength));
            //}
            //Console.WriteLine("Source: \n"+this.Source);
            return null;
        }

        public LineColumn? ConvertToOldLocation(int line, int column)
        {
            var outputIndex = this.LineColumnToIndex(this.Source, line, column);
            var lineData = this.FindLinePragmaForIndex(this.Source, outputIndex);
            if (lineData == null)
            {
                return null;
            }
            //Console.WriteLine("Source: [[[`"+this.Source+"`]]]");
            //foreach(var mapping2 in this.Mappings) {
            //    Console.WriteLine("L: {0} - SL: {1}  SC: {2} SO: {3} SGC: {4}", mapping2.Key, mapping2.Value.StartLine, mapping2.Value.StartColumn, mapping2.Value.StartOffset, mapping2.Value.StartGeneratedColumn);
            //}

            GeneratedCodeMapping mapping;
            if (this.Mappings.TryGetValue(lineData.Item1, out mapping)) {
                //Console.WriteLine("MappingParts: "+outputIndex+", "+mapping.StartGeneratedColumn+", "+mapping.StartColumn+", "+mapping.StartOffset.Value+", "+lineData.Item1+", "+lineData.Item2);
                //Console.WriteLine("Around: [[[`"+this.SourceContext(this.Source, outputIndex)+"`]]]");
                var locationIndex = mapping.StartOffset.Value + (outputIndex-lineData.Item2) - mapping.StartGeneratedColumn + 1;
                //Console.WriteLine("Around: [[[`"+this.SourceContext(this.OriginalSource, locationIndex)+"`]]]");
                return this.IndexToLineColumn(this.OriginalSource, locationIndex);
            } else {
                return null;
            }
        }

        private String SourceContext(String source, int index, int window = 30) {
            try {
                return
                (index < window ? source.Substring(0, index)
                                : source.Substring(index-window, window))
                + "`"
                + (index+window >= source.Length ? source.Substring(index, source.Length-index)
                                                 : source.Substring(index, window));
            } catch (Exception e) {
                return e.ToString();
            }
        }

        private int LineColumnToIndex(String text, int line, int column)
        {
            var count = 1;
            var index = 0;
            while(count < line)
            {
                index = text.IndexOf("\n", index+1);
                count++;
            }
            index += column;
            return index;
        }

        private LineColumn IndexToLineColumn(String text, int index)
        {
            var line = 1;
            var currentLineIndex = 0;
            while(currentLineIndex < index)
            {
                var nextLineIndex = text.IndexOf("\n", currentLineIndex+1);
                if (nextLineIndex > index)
                {
                    break;
                }
                currentLineIndex = nextLineIndex;
                line++;
            }
            var column = index - currentLineIndex;
            return new LineColumn(line, column);
        }

        private int FindIndexForLinePragma(String source, int line)
        {
            //Console.WriteLine("LineSearch: "+line.ToString());
            var lineMatcher = new Regex("^\\s*#line "+line.ToString()+"(\\s*\"[^\"]*\")\r?\n", RegexOptions.Multiline);
            var result = lineMatcher.Match(source);
            if (result.Success)
            {
                return result.Index+result.Length;
            }
            else
            {
                throw new Exception("Line "+line+" not found in :\n"+source);
            }
        }

        private Tuple<int,int> FindLinePragmaForIndex(String source, int index)
        {
            //Console.WriteLine("LineSearch: "+line.ToString());
            var lineMatcher = new Regex("^\\s*#line (\\d+)(\\s*\"[^\"]*\")\r?\n");
            var lineHiddenMatcher = new Regex("^\\s*#line hidden");
            var searchIndex = index;
            while(searchIndex != -1)
            {
                searchIndex = source.LastIndexOf("#", searchIndex);
                if (searchIndex == -1)
                {
                    //Console.Write("# not found at "+index+" in:\n "+source.Substring(index-100, 100)+"\n````````````````\n"+source.Substring(index,100));
                    return null;
                }
                searchIndex = source.LastIndexOf("\n", searchIndex);
                if (searchIndex == -1)
                {
                    //Console.Write("newline not found at "+index+" in:\n "+source.Substring(index-100, 100)+"\n````````````````\n"+source.Substring(index,100));
                    return null;
                }
                //searchIndex += 2;
                var lineTest = lineMatcher.Match(source.Substring(searchIndex));
                if (lineTest.Success)
                {
                    //Console.WriteLine("LineCapture: "+lineTest.Groups[1].Value);
                    //Console.WriteLine("LinePragmaAt: [[[`"+this.SourceContext(source, searchIndex+lineTest.Length)+"`]]]");
                    return Tuple.Create(int.Parse(lineTest.Groups[1].Value), searchIndex+lineTest.Length);
                //}
                //else
                //{
                //    Console.Write("LinePragma not found at "+searchIndex+" in:\n "+source.Substring(Math.Max(searchIndex-100, 0), Math.Min(100, source.Length))+"`"+source.Substring(searchIndex,Math.Min(100, Math.Max(0, source.Length-searchIndex))));
                }
                if (lineHiddenMatcher.IsMatch(source.Substring(searchIndex)))
                {
                    return null;
                //}
                //else
                //{
                //    Console.Write("HiddenPragma not found at "+searchIndex+" in:\n "+source.Substring(Math.Max(searchIndex-100, 0), Math.Min(100, source.Length))+"`"+source.Substring(searchIndex,Math.Min(100, Math.Max(0, source.Length-searchIndex))));
                }
                //searchIndex -= 2;
            }
            return null;
        }
    }
}
