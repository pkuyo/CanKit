namespace EndpointListenerWpf.Resources
{
    public static class I18n
    {
        public static string T(string key)
        {
            var app = System.Windows.Application.Current;
            if (app != null && app.TryFindResource(key) is string s)
                return s;
            return key;
        }
    }
}

