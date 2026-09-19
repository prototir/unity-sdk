using System;
using System.Threading;
using System.Threading.Tasks;

namespace Prototir.Native
{
    /// <summary>One HTTP response, reduced to what the protocol actually decides on.</summary>
    public readonly struct PrototirHttpResponse
    {
        public PrototirHttpResponse(int status, string body)
        {
            Status = status;
            Body = body ?? string.Empty;
        }

        /// <summary>0 means the request never reached the server: no DNS, no route, timeout. That
        /// is different from any status the server chose, and the flow treats it as retryable.</summary>
        public int Status { get; }
        public string Body { get; }

        public bool IsTransport => Status == 0;
    }

    /// <summary>The one thing the protocol needs from the engine. Kept this small so the whole
    /// flow can run against a fake in tests: Unity cannot be invoked from CI, so anything that
    /// touches <c>UnityWebRequest</c> is untestable by construction and everything worth checking
    /// lives on this side of the seam.</summary>
    public interface IPrototirHttp
    {
        Task<PrototirHttpResponse> PostJsonAsync(
            string url, string json, string bearer, CancellationToken ct);
    }

    /// <summary>JSON in and out. Unity supplies <c>JsonUtility</c>, tests supply a real parser;
    /// neither belongs in the protocol itself.</summary>
    public interface IPrototirJson
    {
        string Encode<T>(T value);
        T Decode<T>(string json) where T : new();
    }

    /// <summary>Where a pairing token is kept between runs. A credential, so the implementation
    /// decides the location and the protocol never assumes a path.</summary>
    public interface IPrototirTokenStore
    {
        string Read(string slug);
        void Write(string slug, string token);
        void Clear(string slug);
    }

    /// <summary>Waiting, injectable so a poll loop can be tested without real time passing.</summary>
    public interface IPrototirDelay
    {
        Task WaitAsync(TimeSpan duration, CancellationToken ct);
    }
}
