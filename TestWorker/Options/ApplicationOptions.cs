﻿namespace HttpClientSample.Options
{
    public class ApplicationOptions
    {
        public PolicyOptions Policies { get; set; }

        public TransportHttpOptions TransportHttpClient { get; set; }
    }
}
