using System;

namespace EndpointListenerWpf.Models
{
    public class EndpointInfo
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsCustom { get; set; }

        public override string ToString() => DisplayName;

        public static EndpointInfo Custom()
        {
            return new EndpointInfo
            {
                Id = "custom",
                DisplayName = "Custom",
                IsCustom = true
            };
        }
    }
}

