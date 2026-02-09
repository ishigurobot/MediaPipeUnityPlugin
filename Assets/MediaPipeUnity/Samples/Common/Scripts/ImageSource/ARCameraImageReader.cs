using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

#if USE_ARKIT_WITH_MEDIAPIPEUNITY
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif


public class ARCameraImageReader : MonoBehaviour
{
    public Texture2D Texture2D => _texture2D;
    Texture2D _texture2D;

#if USE_ARKIT_WITH_MEDIAPIPEUNITY

    [SerializeField] ARCameraManager cameraManager;
    [SerializeField] RawImage _rawImage;
    NativeArray<byte> _buffer = new();
    NativeArray<byte> _bufferRot = new();

    // Unityは左手系なので+90で反時計回りに+90
    public enum Rotation
    {
        _0 = 0,
        _90 = 90,
        _180 = 180,
        _270 = 270
    };

    void Awake()
    {
        cameraManager ??= GameObject.FindAnyObjectByType<ARCameraManager>();
    }

    void OnEnable() => cameraManager.frameReceived += OnCameraFrameReceived;
    void OnDisable() => cameraManager.frameReceived -= OnCameraFrameReceived;

    void OnDestroy()
    {
        _buffer.Dispose();
        _bufferRot.Dispose();
        Destroy(_texture2D);
    }

    // 回転版（CPU）
    unsafe void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (!cameraManager.TryAcquireLatestCpuImage(out var image)) return;

        var convertParam = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.None);
        // See how many bytes you need to store the final image.
        int size = image.GetConvertedDataSize(convertParam);
        // Allocate a buffer to store the image.
        if (_buffer.Length != size)
        {
            _buffer.Dispose();
            _buffer = new NativeArray<byte>(size, Allocator.Persistent);
            _bufferRot.Dispose();
            _bufferRot = new NativeArray<byte>(size, Allocator.Persistent);
        }
        // Extract the image data
        image.Convert(convertParam, new IntPtr(_buffer.GetUnsafePtr()), _buffer.Length);
        // The image was converted to RGBA32 format and written into the provided buffer
        // so you can dispose of the XRCpuImage. You must do this or it will leak resources.
        image.Dispose();

        // 入力画像のx,y座標から回転出力画像のx,y座標を計算して返す
        Vector2Int GetTargetPixelPos(Vector2Int inputPos, Vector2Int inputSize, Rotation rot)
        {
            return rot switch
            {
                Rotation._90 => new Vector2Int(inputPos.y, inputSize.x - 1 - inputPos.x),
                Rotation._180 => new Vector2Int(inputSize.x - 1 - inputPos.x, inputSize.y - 1 - inputPos.y),
                Rotation._270 => new Vector2Int(inputSize.y - 1 - inputPos.y, inputPos.x),
                _ => inputPos,
            };
        }

        // 入力画像の縦横サイズから回転出力画像の縦横サイズを計算して返す
        Vector2Int GetTargetPixelSize(Vector2Int inputSize, Rotation rot)
        {
            return rot switch
            {
                Rotation._90 => new Vector2Int(inputSize.y, inputSize.x),
                Rotation._180 => inputSize,
                Rotation._270 => new Vector2Int(inputSize.y, inputSize.x),
                _ => inputSize,
            };
        }

        void CopyWithRotation(NativeArray<byte> src, NativeArray<byte> dst, Rotation rot)
        {
            for (int y = 0; y < image.height; y++)
            {
                for (int x = 0; x < image.width; x++)
                {
                    var newPos = GetTargetPixelPos(new Vector2Int(x, y), image.dimensions, rot);
                    var newSize = GetTargetPixelSize(image.dimensions, rot);
                    // RGBAの4byteごとにコピー
                    NativeArray<byte>.Copy(src, 4 * (x + y * image.width), dst, 4 * (newPos.x + newPos.y * newSize.x), 4);
                }
            }
        }

        switch (Input.deviceOrientation)
        {
            case DeviceOrientation.Portrait:
                CopyWithRotation(_buffer, _bufferRot, Rotation._90);
                ReCreateTexture(ref _texture2D, GetTargetPixelSize(image.dimensions, Rotation._90), convertParam.outputFormat);
                break;
            case DeviceOrientation.LandscapeLeft:
                CopyWithRotation(_buffer, _bufferRot, Rotation._180);
                ReCreateTexture(ref _texture2D, GetTargetPixelSize(image.dimensions, Rotation._180), convertParam.outputFormat);
                break;
            case DeviceOrientation.PortraitUpsideDown:
                CopyWithRotation(_buffer, _bufferRot, Rotation._270);
                ReCreateTexture(ref _texture2D, GetTargetPixelSize(image.dimensions, Rotation._270), convertParam.outputFormat);
                break;
            case DeviceOrientation.LandscapeRight:
            default:
                CopyWithRotation(_buffer, _bufferRot, Rotation._0);
                ReCreateTexture(ref _texture2D, GetTargetPixelSize(image.dimensions, Rotation._0), convertParam.outputFormat);
                break;
        }

        _texture2D.LoadRawTextureData(_bufferRot);
        _texture2D.Apply(false);

        _rawImage.texture = _texture2D;
    }


    // 回転版（低速）
    // unsafe void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    // {
    //     if (!cameraManager.TryAcquireLatestCpuImage(out var image)) return;

    //     var currentConversionParam = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorY);
    //     // See how many bytes you need to store the final image.
    //     int size = image.GetConvertedDataSize(currentConversionParam);
    //     // Allocate a buffer to store the image.
    //     if (_buffer.Length != size)
    //     {
    //         _buffer.Dispose();
    //         _buffer = new NativeArray<byte>(size, Allocator.Persistent);
    //     }
    //     // Extract the image data
    //     image.Convert(currentConversionParam, new IntPtr(_buffer.GetUnsafePtr()), _buffer.Length);
    //     // The image was converted to RGBA32 format and written into the provided buffer
    //     // so you can dispose of the XRCpuImage. You must do this or it will leak resources.
    //     image.Dispose();

    //     // At this point, you can process the image, pass it to a computer vision algorithm, etc.
    //     // In this example, you apply it to a texture to visualize it.
    //     // You've got the data; let's put it into a texture so you can visualize it.
    //     ReCreateTexture(ref tempBuffer, currentConversionParam.outputDimensions, currentConversionParam.outputFormat);
    //     tempBuffer.LoadRawTextureData(_buffer);

    //     if (UnityEngine.Input.deviceOrientation == DeviceOrientation.Portrait)
    //     {
    //         Vector2Int dimension = Vector2Int.zero;
    //         dimension.x = currentConversionParam.outputDimensions.y;
    //         dimension.y = currentConversionParam.outputDimensions.x;
    //         ReCreateTexture(ref _texture2D, dimension, currentConversionParam.outputFormat);

    //         // 90° rotate
    //         for (int y = 0; y < image.height; y++)
    //         {
    //             for (int x = 0; x < image.width; x++)
    //             {
    //                 _texture2D.SetPixel(image.height - y - 1, x, tempBuffer.GetPixel(x, y));
    //             }
    //         }
    //         _texture2D.Apply();
    //     }
    //     else if (UnityEngine.Input.deviceOrientation == DeviceOrientation.LandscapeLeft)
    //     {
    //         ReCreateTexture(ref _texture2D, currentConversionParam.outputDimensions, currentConversionParam.outputFormat);

    //         // 180° rotate
    //         for (int y = 0; y < image.height; y++)
    //         {
    //             for (int x = 0; x < image.width; x++)
    //             {
    //                 _texture2D.SetPixel(image.width - x - 1, image.height - y - 1, tempBuffer.GetPixel(x, y));
    //             }
    //         }
    //         _texture2D.Apply();
    //     }
    //     else if (UnityEngine.Input.deviceOrientation == DeviceOrientation.PortraitUpsideDown)
    //     {
    //         Vector2Int dimension = Vector2Int.zero;
    //         dimension.x = currentConversionParam.outputDimensions.y;
    //         dimension.y = currentConversionParam.outputDimensions.x;
    //         ReCreateTexture(ref _texture2D, dimension, currentConversionParam.outputFormat);

    //         // 270° rotate
    //         for (int y = 0; y < image.height; y++)
    //         {
    //             for (int x = 0; x < image.width; x++)
    //             {
    //                 _texture2D.SetPixel(y, image.width - x - 1, tempBuffer.GetPixel(x, y));
    //             }
    //         }
    //         _texture2D.Apply();
    //     }
    //     else
    //     {
    //         _texture2D = tempBuffer;
    //     }
    //     _rawImage.texture = _texture2D;
    // }

    protected void ReCreateTexture(ref Texture2D tex, in Vector2Int size, TextureFormat format)
    {
        if (tex != null && tex.width == size.x && tex.height == size.y) return;// Check necessity
        Debug.Log($"ReCreateTexture {tex} {tex?.width} x {tex?.height} -> {size}");
        if (tex != null) Destroy(tex);
        tex = new Texture2D(size.x, size.y, format, false);
    }
#endif
}