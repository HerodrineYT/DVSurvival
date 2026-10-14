namespace DVSurvival.Core
{
    public static class NormalizedName
    {
        private static bool Skip(char c) => c == '_' || c == '-' || c == ' ';
        public static bool Contains(string value, string token, bool exact = false)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) return false;
            for (int start = 0; start < value.Length; start++)
            {
                if (Skip(value[start])) continue;
                int index = start, matched = 0;
                while (index < value.Length && matched < token.Length)
                {
                    char c = value[index++];
                    if (Skip(c)) continue;
                    if (char.ToLowerInvariant(c) != token[matched]) break;
                    matched++;
                }
                if (matched == token.Length)
                {
                    if (!exact) return true;
                    while (index < value.Length && Skip(value[index])) index++;
                    return index == value.Length;
                }
                if (exact) return false;
            }
            return false;
        }
    }
}
