using SixLabors.ImageSharp.PixelFormats;

namespace Penumbra.Import.Textures;

public partial class CombinedTexture
{
    private Matrix4x4 _multiplierLeft  = Matrix4x4.Identity;
    private Vector4   _constantLeft    = Vector4.Zero;
    private Matrix4x4 _multiplierRight = Matrix4x4.Identity;
    private Vector4   _constantRight   = Vector4.Zero;
    private int       _offsetX;
    private int       _offsetY;
    private CombineOp _combineOp    = CombineOp.Over;
    private ResizeOp  _resizeOp     = ResizeOp.None;
    private Channels  _copyChannels = Channels.Red | Channels.Green | Channels.Blue | Channels.Alpha;

    private RgbaPixelData _leftPixels  = RgbaPixelData.Empty;
    private RgbaPixelData _rightPixels = RgbaPixelData.Empty;

    private const float OneThird = 1.0f / 3.0f;
    private const float RWeight  = 0.2126f;
    private const float GWeight  = 0.7152f;
    private const float BWeight  = 0.0722f;    

    private Vector4 DataLeft(int offset)
        => CappedVector(_leftPixels.PixelData, offset, _multiplierLeft, _constantLeft);

    private Vector4 DataRight(int offset)
        => CappedVector(_rightPixels.PixelData, offset, _multiplierRight, _constantRight);

    private Vector4 DataRight(int x, int y)
    {
        x += _offsetX;
        y += _offsetY;
        if (x < 0 || x >= _rightPixels.Width || y < 0 || y >= _rightPixels.Height)
            return Vector4.Zero;

        var offset = (y * _rightPixels.Width + x) * 4;
        return CappedVector(_rightPixels.PixelData, offset, _multiplierRight, _constantRight);
    }

    private void AddPixelsMultiplied(int y, ParallelLoopState _)
    {
        for (var x = 0; x < _leftPixels.Width; ++x)
        {
            var offset = (_leftPixels.Width * y + x) * 4;
            var left   = DataLeft(offset);
            var right  = DataRight(x, y);
            var alpha  = right.W + left.W * (1 - right.W);
            var rgba = alpha == 0
                ? new Rgba32()
                : new Rgba32(((right * right.W + left * left.W * (1 - right.W)) / alpha) with { W = alpha });
            _centerStorage.RgbaPixels[offset]     = rgba.R;
            _centerStorage.RgbaPixels[offset + 1] = rgba.G;
            _centerStorage.RgbaPixels[offset + 2] = rgba.B;
            _centerStorage.RgbaPixels[offset + 3] = rgba.A;
        }
    }

    private void ReverseAddPixelsMultiplied(int y, ParallelLoopState _)
    {
        for (var x = 0; x < _leftPixels.Width; ++x)
        {
            var offset = (_leftPixels.Width * y + x) * 4;
            var left   = DataLeft(offset);
            var right  = DataRight(x, y);
            var alpha  = left.W + right.W * (1 - left.W);
            var rgba = alpha == 0
                ? new Rgba32()
                : new Rgba32(((left * left.W + right * right.W * (1 - left.W)) / alpha) with { W = alpha });
            _centerStorage.RgbaPixels[offset]     = rgba.R;
            _centerStorage.RgbaPixels[offset + 1] = rgba.G;
            _centerStorage.RgbaPixels[offset + 2] = rgba.B;
            _centerStorage.RgbaPixels[offset + 3] = rgba.A;
        }
    }

    private void ChannelMergePixelsMultiplied(int y, ParallelLoopState _)
    {
        var channels = _copyChannels;
        for (var x = 0; x < _leftPixels.Width; ++x)
        {
            var offset = (_leftPixels.Width * y + x) * 4;
            var left   = DataLeft(offset);
            var right  = DataRight(x, y);
            var rgba = new Rgba32((channels & Channels.Red) != 0 ? right.X : left.X,
                (channels & Channels.Green) != 0 ? right.Y : left.Y,
                (channels & Channels.Blue) != 0 ? right.Z : left.Z,
                (channels & Channels.Alpha) != 0 ? right.W : left.W);
            _centerStorage.RgbaPixels[offset]     = rgba.R;
            _centerStorage.RgbaPixels[offset + 1] = rgba.G;
            _centerStorage.RgbaPixels[offset + 2] = rgba.B;
            _centerStorage.RgbaPixels[offset + 3] = rgba.A;
        }
    }

    private void MultiplyPixelsLeft(int y, ParallelLoopState _)
    {
        for (var x = 0; x < _leftPixels.Width; ++x)
        {
            var offset = (_leftPixels.Width * y + x) * 4;
            var left   = DataLeft(offset);
            var rgba   = new Rgba32(left);
            _centerStorage.RgbaPixels[offset]     = rgba.R;
            _centerStorage.RgbaPixels[offset + 1] = rgba.G;
            _centerStorage.RgbaPixels[offset + 2] = rgba.B;
            _centerStorage.RgbaPixels[offset + 3] = rgba.A;
        }
    }

    private void MultiplyPixelsRight(int y, ParallelLoopState _)
    {
        for (var x = 0; x < _rightPixels.Width; ++x)
        {
            var offset = (_rightPixels.Width * y + x) * 4;
            var right  = DataRight(offset);
            var rgba   = new Rgba32(right);
            _centerStorage.RgbaPixels[offset]     = rgba.R;
            _centerStorage.RgbaPixels[offset + 1] = rgba.G;
            _centerStorage.RgbaPixels[offset + 2] = rgba.B;
            _centerStorage.RgbaPixels[offset + 3] = rgba.A;
        }
    }

    private (int Width, int Height) CombineImage()
    {
        var combineOp = GetActualCombineOp();
        var resizeOp  = GetActualResizeOp(_resizeOp, combineOp);

        var left  = resizeOp != ResizeOp.RightOnly ? RgbaPixelData.FromTexture(_left) : RgbaPixelData.Empty;
        var right = resizeOp != ResizeOp.LeftOnly ? RgbaPixelData.FromTexture(_right) : RgbaPixelData.Empty;

        var targetSize = resizeOp switch
        {
            ResizeOp.RightOnly => right.Size,
            ResizeOp.ToRight   => right.Size,
            _                  => left.Size,
        };

        try
        {
            _centerStorage.RgbaPixels = RgbaPixelData.NewPixelData(targetSize);
            _centerStorage.Type       = TextureType.Bitmap;

            _leftPixels = resizeOp switch
            {
                ResizeOp.RightOnly => RgbaPixelData.Empty,
                _                  => left.Resize(targetSize),
            };
            _rightPixels = resizeOp switch
            {
                ResizeOp.LeftOnly => RgbaPixelData.Empty,
                ResizeOp.None     => right,
                _                 => right.Resize(targetSize),
            };

            Parallel.For(0, targetSize.Height, combineOp switch
            {
                CombineOp.Over          => AddPixelsMultiplied,
                CombineOp.Under         => ReverseAddPixelsMultiplied,
                CombineOp.LeftMultiply  => MultiplyPixelsLeft,
                CombineOp.RightMultiply => MultiplyPixelsRight,
                CombineOp.CopyChannels  => ChannelMergePixelsMultiplied,
                _                       => throw new InvalidOperationException($"Cannot combine images with operation {combineOp}"),
            });
        }
        finally
        {
            _leftPixels  = RgbaPixelData.Empty;
            _rightPixels = RgbaPixelData.Empty;
        }

        return targetSize;
    }

    private static Vector4 CappedVector(IReadOnlyList<byte> bytes, int offset, Matrix4x4 transform, Vector4 constant)
    {
        if (bytes.Count == 0)
            return Vector4.Zero;

        var rgba        = new Rgba32(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
        var transformed = Vector4.Transform(rgba.ToVector4(), transform) + constant;

        transformed.X = Math.Clamp(transformed.X, 0, 1);
        transformed.Y = Math.Clamp(transformed.Y, 0, 1);
        transformed.Z = Math.Clamp(transformed.Z, 0, 1);
        transformed.W = Math.Clamp(transformed.W, 0, 1);
        return transformed;
    }
}
