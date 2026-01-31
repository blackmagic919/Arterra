using UnityEngine;
namespace Arterra.Utils {
    public class SaveTextureToFileUtility
    {
        public enum SaveTextureFileFormat
        {
            EXR, JPG, PNG, TGA
        };

        /// <summary>
        /// Saves a Texture2D to disk with the specified filename and image format
        /// </summary>
        /// <param name="tex"></param>
        /// <param name="filePath"></param>
        /// <param name="fileFormat"></param>
        /// <param name="jpgQuality"></param>
        static public void SaveTexture2DToFile(Texture2D tex, string filePath, SaveTextureFileFormat fileFormat, int jpgQuality = 95)
        {
            switch (fileFormat)
            {
                case SaveTextureFileFormat.EXR:
                    System.IO.File.WriteAllBytes(filePath + ".exr", tex.EncodeToEXR());
                    break;
                case SaveTextureFileFormat.JPG:
                    System.IO.File.WriteAllBytes(filePath + ".jpg", tex.EncodeToJPG(jpgQuality));
                    break;
                case SaveTextureFileFormat.PNG:
                    System.IO.File.WriteAllBytes(filePath + ".png", tex.EncodeToPNG());
                    break;
                case SaveTextureFileFormat.TGA:
                    System.IO.File.WriteAllBytes(filePath + ".tga", tex.EncodeToTGA());
                    break;
            }
        }


        /// <summary>
        /// Saves a RenderTexture to disk with the specified filename and image format
        /// </summary>
        /// <param name="renderTexture"></param>
        /// <param name="filePath"></param>
        /// <param name="fileFormat"></param>
        /// <param name="jpgQuality"></param>
        static public void SaveRenderTextureToFile(RenderTexture renderTexture, string filePath, SaveTextureFileFormat fileFormat = SaveTextureFileFormat.PNG, int jpgQuality = 95)
        {
            Texture2D tex;
            if (fileFormat != SaveTextureFileFormat.EXR)
                tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false, false);
            else
                tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBAFloat, false, true);
            var oldRt = RenderTexture.active;
            RenderTexture.active = renderTexture;
            tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            tex.Apply();
            RenderTexture.active = oldRt;
            SaveTexture2DToFile(tex, filePath, fileFormat, jpgQuality);
            if (Application.isPlaying)
                Object.Destroy(tex);
            else
                Object.DestroyImmediate(tex);

        }

        /// <summary> Reads an image at a given file path and converts it into a Sprite. </summary>
        /// <param name="filePath">The file path of the image file</param>
        /// <param name="fileFormat"></param>
        /// <returns>The sprite containing the read image or null if no file is found 
        /// or the file is formatted incorrectly. </returns>
        static public Sprite LoadImageToSprite(string filePath, SaveTextureFileFormat fileFormat = SaveTextureFileFormat.PNG){
            switch (fileFormat) {
                case SaveTextureFileFormat.EXR:
                    filePath += ".exr";
                    break;
                case SaveTextureFileFormat.JPG:
                    filePath += ".jpg";
                    break;
                case SaveTextureFileFormat.PNG:
                    filePath += ".png";
                    break;
                case SaveTextureFileFormat.TGA:
                    filePath += ".tga";
                    break;
            }

            // If file doesn't exist, return null
            if (!System.IO.File.Exists(filePath))
                return null;

            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(filePath);
                if (bytes == null || bytes.Length == 0)
                    return null;

                // Create texture; let Unity infer format
                Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!tex.LoadImage(bytes)) {
                    Object.Destroy(tex);
                    return null;
                }

                // Create sprite with full rect, centered pivot
                return Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f   // default pixels-per-unit
                );
            }
            catch
            {
                return null;
            }
        }

    }
}