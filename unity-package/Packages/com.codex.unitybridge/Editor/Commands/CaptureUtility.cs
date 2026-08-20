using System;
using System.Collections.Generic;
using System.IO;
using Codex.UnityBridge.Protocol;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class CaptureUtility
    {
        private const int MaxDimension = 4096;
        private const int MaxPixels = 16777216;

        public static IDictionary<string, object> CaptureCamera(
            Camera camera, int width, int height, bool transparent, string prefix)
        {
            ValidateDimensions(width, height);
            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldTarget = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            CameraClearFlags oldFlags = camera.clearFlags;
            Color oldColor = camera.backgroundColor;
            Texture2D texture = null;
            try
            {
                camera.targetTexture = renderTexture;
                if (transparent)
                {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0, 0, 0, 0);
                }
                camera.Render();
                RenderTexture.active = renderTexture;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                return WritePng(texture.EncodeToPNG(), width, height, camera.name, prefix);
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.clearFlags = oldFlags;
                camera.backgroundColor = oldColor;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        public static IDictionary<string, object> CaptureTexture(Texture texture, int width, int height, string prefix)
        {
            ValidateDimensions(width, height);
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture oldActive = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return WritePng(readable.EncodeToPNG(), width, height, texture.name, prefix);
            }
            finally
            {
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        private static IDictionary<string, object> WritePng(
            byte[] data, int width, int height, string source, string prefix)
        {
            if (data == null || data.Length == 0)
                throw new ProtocolException("COMMAND_FAILED", "Unity produced an empty PNG capture.");
            string fullPath = ProjectPathUtility.CreateCapturePath(prefix);
            File.WriteAllBytes(fullPath, data);
            return new Dictionary<string, object>
            {
                { "ready", true }, { "capturePath", ProjectPathUtility.ToProjectRelativePath(fullPath) },
                { "mimeType", "image/png" }, { "width", width }, { "height", height },
                { "source", source }, { "timestamp", DateTime.UtcNow.ToString("o") },
                { "playMode", UnityEditor.EditorApplication.isPlaying }
            };
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width < 64 || height < 64 || width > MaxDimension || height > MaxDimension
                || (long)width * height > MaxPixels)
                throw new ProtocolException("INVALID_ARGUMENT", "Capture dimensions exceed safe limits.",
                    new Dictionary<string, object> { { "maxDimension", MaxDimension }, { "maxPixels", MaxPixels } });
        }
    }
}
