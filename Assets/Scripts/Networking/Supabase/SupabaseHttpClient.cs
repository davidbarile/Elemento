using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace Elemento.Networking.Supabase
{
    internal sealed class SupabaseHttpException : InvalidOperationException
    {
        public long ResponseCode { get; }
        public string Reason { get; }
        public string ResponseBody { get; }

        public SupabaseHttpException(long inResponseCode, string inReason, string inResponseBody)
            : base("Supabase HTTP " + inResponseCode + ": " + inReason)
        {
            this.ResponseCode = inResponseCode;
            this.Reason = inReason;
            this.ResponseBody = inResponseBody;
        }
    }

    internal sealed class SupabaseHttpResponseData
    {
        public long StatusCode { get; set; }
        public string Body { get; set; }
    }

    internal sealed class SupabaseHttpClient
    {
        private const int TimeoutSeconds = 30;
        private readonly MonoBehaviour runner;

        public SupabaseHttpClient(MonoBehaviour inRunner)
        {
            this.runner = inRunner != null ? inRunner : throw new ArgumentNullException(nameof(inRunner));
        }

        public async Task<string> SendAsync(string inPath, string inBody, string inAccessToken, string inMethod = "POST")
        {
            return (await SendWithStatusAsync(inPath, inBody, inAccessToken, inMethod)).Body;
        }

        public async Task<SupabaseHttpResponseData> SendWithStatusAsync(string inPath, string inBody,
            string inAccessToken, string inMethod = "POST")
        {
            var completion = new TaskCompletionSource<SupabaseHttpResponseData>();
            try
            {
                this.runner.StartCoroutine(Send(inPath, inBody, inAccessToken, inMethod, completion));
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }

            Task finished = await Task.WhenAny(completion.Task, Task.Delay((TimeoutSeconds + 5) * 1000));
            if (finished != completion.Task)
                throw new TimeoutException("Supabase HTTP request timed out.");
            return await completion.Task;
        }

        internal static string BuildFailureWarning(string inPrefix, long inStatusCode,
            string inReason, string inResponseBody)
        {
            string message = inPrefix + ": HTTP " + inStatusCode + ", reason="
                + (string.IsNullOrEmpty(inReason) ? "unknown" : inReason);
            if ((Application.isEditor || Debug.isDebugBuild) && !string.IsNullOrEmpty(inResponseBody))
                message += ", body=" + (inResponseBody.Length <= 2048
                    ? inResponseBody : inResponseBody.Substring(0, 2048) + "...");
            return message;
        }

        private IEnumerator Send(string inPath, string inBody, string inAccessToken, string inMethod,
            TaskCompletionSource<SupabaseHttpResponseData> inCompletion)
        {
            using (var request = new UnityWebRequest(SupabaseConfig.Url + inPath, inMethod))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = TimeoutSeconds;
                if (inBody != null)
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(inBody));
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", SupabaseConfig.AnonKey);
                request.SetRequestHeader("Authorization", "Bearer " + (inAccessToken ?? SupabaseConfig.AnonKey));
                yield return request.SendWebRequest();
                string responseBody = request.downloadHandler?.text ?? string.Empty;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string reason = GetReason(responseBody, request.error);
                    Debug.LogWarning(BuildFailureWarning(
                        "Supabase request rejected (" + inPath + ")", request.responseCode,
                        reason, responseBody));
                    inCompletion.TrySetException(new SupabaseHttpException(
                        request.responseCode, reason, responseBody));
                }
                else
                {
                    inCompletion.TrySetResult(new SupabaseHttpResponseData
                    {
                        StatusCode = request.responseCode,
                        Body = responseBody
                    });
                }
            }
        }

        private static string GetReason(string inBody, string inFallback)
        {
            if (!string.IsNullOrEmpty(inBody))
            {
                try
                {
                    var error = NetJson.Deserialize<SupabaseErrorData>(inBody);
                    if (!string.IsNullOrEmpty(error?.Reason))
                        return error.Reason;
                    if (!string.IsNullOrEmpty(error?.Error))
                        return error.Error;
                    if (!string.IsNullOrEmpty(error?.LowerError))
                        return error.LowerError;
                }
                catch (Exception)
                {
                }
            }
            return string.IsNullOrEmpty(inFallback) ? "unknown" : inFallback;
        }

        [Serializable]
        private sealed class SupabaseErrorData
        {
            public string Error;
            [NetJsonName("error")] public string LowerError;
            public string Reason;
        }
    }
}
