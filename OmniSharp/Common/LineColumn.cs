namespace OmniSharp.Common
{
    public struct LineColumn
    {
        public int Line;
        public int Column;

        public LineColumn(int line, int column)
        {
            Line = line;
            Column = column;
        }
    }
}
