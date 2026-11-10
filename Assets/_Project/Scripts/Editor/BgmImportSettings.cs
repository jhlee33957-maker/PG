using UnityEditor;
using UnityEngine;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// Resources/Bgm 안의 배경음악은 용량이 커서(wav 수십 MB) 압축하고 스트리밍으로 읽게 맞춘다.
    /// 그대로 두면 빌드 용량과 로딩이 커진다.
    /// </summary>
    internal sealed class BgmImportSettings : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Resources/Bgm/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Folder, System.StringComparison.OrdinalIgnoreCase)) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            importer.loadInBackground = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
        }
    }
}
