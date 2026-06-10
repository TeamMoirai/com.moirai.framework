using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Moirai.Atropos
{
    public static partial class HttpUtility
    {
        /// <summary>
        /// 发起HTTP GET请求。
        /// </summary>
        /// <param name="url">请求的目标URL。</param>
        /// <param name="actionResult">请求完成后的回调，接收UnityWebRequest对象用于处理结果。</param>
        public static void Get(string url, Action<UnityWebRequest> actionResult)
        {
            UnityUtility.StartCoroutine(OnGet(url, actionResult));
        }

        /// <summary>
        /// 下载文件到本地路径。
        /// </summary>
        /// <param name="url">文件下载URL。</param>
        /// <param name="downloadFilePathAndName">文件保存路径（包含文件名）。</param>
        /// <param name="actionResult">下载完成后的回调，接收UnityWebRequest对象用于处理结果。</param>
        public static void DownloadFile(string url, string downloadFilePathAndName, Action<UnityWebRequest> actionResult)
        {
            UnityUtility.StartCoroutine(OnDownloadFile(url, downloadFilePathAndName, actionResult));
        }

        /// <summary>
        /// 获取网络纹理资源。
        /// </summary>
        /// <param name="url">纹理资源URL。</param>
        /// <param name="actionResult">获取完成后的回调，成功时返回Texture2D对象，失败返回null。</param>
        public static void GetTexture(string url, Action<Texture2D> actionResult)
        {
            UnityUtility.StartCoroutine(OnGetTexture(url, actionResult));
        }

        /// <summary>
        /// 获取网络音频资源。
        /// </summary>
        /// <param name="url">音频资源URL。</param>
        /// <param name="actionResult">获取完成后的回调，成功时返回AudioClip对象。</param>
        /// <param name="audioType">音频类型（默认WAV格式）。</param>
        public static void GetAudioClip(string url, Action<AudioClip> actionResult, UnityEngine.AudioType audioType = UnityEngine.AudioType.WAV)
        {
            UnityUtility.StartCoroutine(OnGetAudioClip(url, actionResult, audioType));
        }

        /// <summary>
        /// 发起HTTP POST请求。
        /// </summary>
        /// <param name="serverURL">请求的目标URL。</param>
        /// <param name="lstformData">POST表单数据列表。</param>
        /// <param name="actionResult">请求完成后的回调，接收UnityWebRequest对象用于处理结果。</param>
        public static void Post(string serverURL, List<IMultipartFormSection> lstformData, Action<UnityWebRequest> actionResult)
        {
            UnityUtility.StartCoroutine(OnPost(serverURL, lstformData, actionResult));
        }

        /// <summary>
        /// 使用PUT方法上传二进制数据。
        /// </summary>
        /// <param name="url">上传目标URL。</param>
        /// <param name="contentBytes">要上传的二进制内容。</param>
        /// <param name="actionResult">上传完成后的回调，返回是否成功。</param>
        public static void UploadByPut(string url, byte[] contentBytes, Action<bool> actionResult)
        {
            UnityUtility.StartCoroutine(OnUploadByPut(url, contentBytes, actionResult, ""));
        }

        #region Private Coroutine Implementations

        /// <summary>
        /// GET请求协程实现。
        /// </summary>
        private static IEnumerator OnGet(string url, Action<UnityWebRequest> actionResult)
        {
            using UnityWebRequest uwr = UnityWebRequest.Get(url);
            yield return uwr.SendWebRequest();
            actionResult?.Invoke(uwr);
        }

        /// <summary>
        /// 文件下载协程实现。
        /// </summary>
        /// <remarks>使用DownloadHandlerFile直接保存到本地路径。</remarks>
        private static IEnumerator OnDownloadFile(string url, string downloadFilePathAndName, Action<UnityWebRequest> actionResult)
        {
            UnityWebRequest uwr = new UnityWebRequest(url, "GET");
            uwr.downloadHandler = new DownloadHandlerFile(downloadFilePathAndName);
            yield return uwr.SendWebRequest();
            actionResult?.Invoke(uwr);
        }

        /// <summary>
        /// 获取纹理协程实现。
        /// </summary>
        /// <remarks>使用DownloadHandlerTexture自动处理纹理数据。</remarks>
        private static IEnumerator OnGetTexture(string url, Action<Texture2D> actionResult)
        {
            UnityWebRequest uwr = new UnityWebRequest(url);
            DownloadHandlerTexture downloadTexture = new DownloadHandlerTexture(true);
            uwr.downloadHandler = downloadTexture;
            yield return uwr.SendWebRequest();
            Texture2D texture2D = null;
            if (uwr.result == UnityWebRequest.Result.Success)
            {
                texture2D = downloadTexture.texture;
            }

            actionResult?.Invoke(texture2D);
        }

        /// <summary>
        /// 获取音频剪辑协程实现。
        /// </summary>
        /// <remarks>使用UnityWebRequestMultimedia处理音频流。</remarks>
        private static IEnumerator OnGetAudioClip(string url, Action<AudioClip> actionResult, UnityEngine.AudioType audioType = UnityEngine.AudioType.WAV)
        {
            using UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(url, audioType);
            yield return uwr.SendWebRequest();
            if (uwr.result == UnityWebRequest.Result.Success && actionResult != null)
            {
                actionResult(DownloadHandlerAudioClip.GetContent(uwr));
            }
        }

        /// <summary>
        /// POST请求协程实现。
        /// </summary>
        /// <remarks>支持多部分表单数据提交。</remarks>
        private static IEnumerator OnPost(string serverURL, List<IMultipartFormSection> lstformData, Action<UnityWebRequest> actionResult)
        {
            UnityWebRequest uwr = UnityWebRequest.Post(serverURL, lstformData);
            yield return uwr.SendWebRequest();
            actionResult?.Invoke(uwr);
        }

        /// <summary>
        /// PUT上传协程实现。
        /// </summary>
        /// <remarks>使用UploadHandlerRaw处理二进制数据上传。</remarks>
        private static IEnumerator OnUploadByPut(string url, byte[] contentBytes, Action<bool> actionResult, string contentType = "application/octet-stream")
        {
            UnityWebRequest uwr = new UnityWebRequest();
            UploadHandler uploadHandler = new UploadHandlerRaw(contentBytes);
            uploadHandler.contentType = contentType;
            uwr.uploadHandler = uploadHandler;
            yield return uwr.SendWebRequest();
            bool flag = uwr.result == UnityWebRequest.Result.Success;
            actionResult?.Invoke(flag);
        }

        #endregion
    }
}