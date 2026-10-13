using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Elemento.Networking.Supabase
{
    internal sealed class SupabaseHttpException : InvalidOperationException
    {
        public long ResponseCode { get; }

        public SupabaseHttpException(long inResponseCode, string inMessage) : base(inMessage)
        {
            this.ResponseCode = inResponseCode;
        }
    }

    internal sealed class SupabaseHttpClient
    {
        private readonly MonoBehaviour runner;

        public SupabaseHttpClient(MonoBehaviour inRunner)
        {
            this.runner = inRunner != null ? inRunner : throw new ArgumentNullException(nameof(inRunner));
        }

        public Task<string> SendAsync(string inPath, string inBody, string inAccessToken, string inMethod = "POST")
        {
            var completion = new TaskCompletionSource<string>();
            this.runner.StartCoroutine(Send(inPath, inBody, inAccessToken, inMethod, completion));
            return completion.Task;
        }

        private IEnumerator Send(string inPath, string inBody, string inAccessToken, string inMethod, TaskCompletionSource<string> inCompletion)
        {
            using (var request = new UnityWebRequest(SupabaseConfig.Url + inPath, inMethod))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 30;
                if (inBody != null)
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(inBody));
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", SupabaseConfig.AnonKey);
                request.SetRequestHeader("Authorization", "Bearer " + (inAccessToken ?? SupabaseConfig.AnonKey));
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                    inCompletion.TrySetException(new SupabaseHttpException(request.responseCode,
                        "Supabase HTTP " + request.responseCode + ": " + request.downloadHandler.text));
                else
                    inCompletion.TrySetResult(request.downloadHandler.text);
            }
        }
    }
}
