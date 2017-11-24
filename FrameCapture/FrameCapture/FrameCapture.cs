using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Capture.Frames;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using System.Threading;
using Windows.UI.Core;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Storage.Streams;

namespace FrameCapture
{
    public class FrameCapture
    {
        private MediaFrameSourceInfo _mediaFrameSourceInfo;
        private MediaFrameSourceGroup _mediaFrameSourceGroup;

        private SoftwareBitmap _backBuffer;

        public FrameCapture()
        {
#pragma warning disable 4014
            Init();
#pragma warning restore 4014
        }

        private async Task Init()
        {
            try
            {
                var mediaFrameSourceGroups = await MediaFrameSourceGroup.FindAllAsync();

                foreach (var sourceGroup in mediaFrameSourceGroups)
                {
                    foreach (var sourceInfo in sourceGroup.SourceInfos)
                    {
                        if ((sourceInfo.MediaStreamType == MediaStreamType.VideoRecord) &&
                            sourceInfo.SourceKind == MediaFrameSourceKind.Color)
                        {
                            _mediaFrameSourceInfo = sourceInfo;
                            break;
                        }
                    }

                    if (_mediaFrameSourceInfo != null)
                    {
                        _mediaFrameSourceGroup = sourceGroup;
                        break;
                    }
                }

                if (_mediaFrameSourceGroup == null || _mediaFrameSourceInfo == null)
                {
                    return;
                }

                var mediaCapture = new MediaCapture();

                var settings = new MediaCaptureInitializationSettings
                {
                    SourceGroup = _mediaFrameSourceGroup,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video
                };

                await mediaCapture.InitializeAsync(settings);

                var frameSource = mediaCapture.FrameSources[_mediaFrameSourceInfo.Id];
                var preferredFormat = frameSource.SupportedFormats.FirstOrDefault();

                if (preferredFormat == null)
                {
                    return;
                }

                await frameSource.SetFormatAsync(preferredFormat);

                var frameReader = await mediaCapture.CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Argb32);
                frameReader.FrameArrived += FrameReader_FrameArrived;
                await frameReader.StartAsync();
            }
            catch (Exception e)
            {
                var x = 2;
            }
        }

        private void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            var mediaFrameReference = sender.TryAcquireLatestFrame();
            var videoMediaFrame = mediaFrameReference?.VideoMediaFrame;
            var softwareBitmap = videoMediaFrame?.SoftwareBitmap;

            if (softwareBitmap == null)
            {
                return;
            }

            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            softwareBitmap = Interlocked.Exchange(ref _backBuffer, softwareBitmap);
            softwareBitmap?.Dispose();
        }

        public async Task<byte[]> GetLatestFrame()
        {
            var latestBitmap = Interlocked.Exchange(ref _backBuffer, null);
            using (var stream = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
                encoder.SetSoftwareBitmap(latestBitmap);

                await encoder.FlushAsync();

                var buffer = new Windows.Storage.Streams.Buffer((uint) stream.Size);
                await stream.ReadAsync(buffer, (uint) stream.Size, InputStreamOptions.None);

                return buffer.ToArray();
            }
        }
    }
}
