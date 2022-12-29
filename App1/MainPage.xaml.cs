using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Microsoft.MixedReality.WebRTC;
using System.Diagnostics;
using Windows.Media.Capture;
using Windows.ApplicationModel;
using TestAppUwp.Video;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.MediaProperties;

// 빈 페이지 항목 템플릿에 대한 설명은 https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x412에 나와 있습니다.

namespace App1
{
    /// <summary>
    /// 자체적으로 사용하거나 프레임 내에서 탐색할 수 있는 빈 페이지입니다.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private PeerConnection _peerConnection;

        DeviceAudioTrackSource _microphoneSource;
        DeviceVideoTrackSource _webcamSource;
        LocalAudioTrack _localAudioTrack;
        LocalVideoTrack _localVideoTrack;
        Transceiver _audioTransceiver;
        Transceiver _videoTransceiver;
        private MediaStreamSource _localVideoSource;
        private VideoBridge _localVideoBridge = new VideoBridge(3);
        private bool _localVideoPlaying = false;
        private object _localVideoLock = new object();

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            Application.Current.Suspending += App_Suspending;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var settings = new MediaCaptureInitializationSettings();
            settings.StreamingCaptureMode = StreamingCaptureMode.AudioAndVideo;
            var capture = new MediaCapture();
            await capture.InitializeAsync(settings);
            

            _peerConnection = new PeerConnection();
            var config = new PeerConnectionConfiguration
            {
                IceServers = new List<IceServer> {
            new IceServer{ Urls = { "stun:stun.l.google.com:19302" } }
            }
            };
            await _peerConnection.InitializeAsync(config);

            Debugger.Log(0, "", "Peer connection initialized successfully.\n");


            IReadOnlyList<VideoCaptureDevice> deviceList =
                await DeviceVideoTrackSource.GetCaptureDevicesAsync();

            foreach (var device in deviceList)
            {
                Debugger.Log(0, "", $"Webcam {device.name} (id: {device.id})\n");
            }

            _webcamSource = await DeviceVideoTrackSource.CreateAsync();
            _webcamSource.I420AVideoFrameReady += LocalI420AFrameReady;
            var videoTrackConfig = new LocalVideoTrackInitConfig
            {
                trackName = "webcam_track"
            };
            _localVideoTrack = LocalVideoTrack.CreateFromSource(_webcamSource, videoTrackConfig);

            _microphoneSource = await DeviceAudioTrackSource.CreateAsync();
            var audioTrackConfig = new LocalAudioTrackInitConfig
            {
                trackName = "microphone_track"
            };
            _localAudioTrack = LocalAudioTrack.CreateFromSource(_microphoneSource, audioTrackConfig);

            _audioTransceiver = _peerConnection.AddTransceiver(MediaKind.Audio);
            _videoTransceiver = _peerConnection.AddTransceiver(MediaKind.Video);
            _audioTransceiver.LocalAudioTrack = _localAudioTrack;
            _videoTransceiver.LocalVideoTrack = _localVideoTrack;
        }

        private void LocalI420AFrameReady(I420AVideoFrame frame)
        {
            lock(_localVideoLock)
            {
                if(!_localVideoPlaying)
                {
                    _localVideoPlaying = true;

                    uint width = frame.width;
                    uint height = frame.height;

                    RunOnMainThread(() =>
                    {
                        int framerate = 30;
                        _localVideoSource = CreateI420VideoStreamSource(width, height, framerate);
                        var localVideoPlayer = new MediaPlayer();
                        localVideoPlayer.Source = MediaSource.CreateFromMediaStreamSource(
                            _localVideoSource);
                        localVideoPlayerElement.SetMediaPlayer(localVideoPlayer);
                        localVideoPlayer.Play();
                    });
                }
            }

            _localVideoBridge.HandleIncomingVideoFrame(frame);
        }

        private void RunOnMainThread(Windows.UI.Core.DispatchedHandler handler)
        {
            if(Dispatcher.HasThreadAccess)
            {
                handler.Invoke();
            }
            else
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, handler);
            }
        }

        private void App_Suspending(object sender, SuspendingEventArgs e)
        {
            if(_peerConnection != null)
            {
                _peerConnection.Close();
                _peerConnection.Dispose();
                _peerConnection = null;

                localVideoPlayerElement.SetMediaPlayer(null);
            }
            //localVideoPlayerElement.SetMediaPlayer(null);
        }

        private MediaStreamSource CreateI420VideoStreamSource(uint width, uint height, int framerate)
        {
            if(width == 0)
            {
                throw new ArgumentException("Invalid zero width for video.", "width");
            }

            if(height == 0)
            {
                throw new ArgumentException("Invalid zero height for video.", "height");
            }

            var videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Iyuv, width, height);
            var videoStreamDesc = new VideoStreamDescriptor(videoProperties);
            videoStreamDesc.EncodingProperties.FrameRate.Numerator = (uint)framerate;
            videoStreamDesc.EncodingProperties.FrameRate.Denominator = 1;

            videoStreamDesc.EncodingProperties.Bitrate = ((uint)framerate * width * height * 12);
            var VideoStreamSource = new MediaStreamSource(videoStreamDesc);
            VideoStreamSource.BufferTime = TimeSpan.Zero;
            VideoStreamSource.SampleRequested += OnMediaStreamSourceRequested;
            VideoStreamSource.IsLive = true;
            VideoStreamSource.CanSeek = false;
            return VideoStreamSource;
        }

        private void OnMediaStreamSourceRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            VideoBridge videoBridge;
            if (sender == _localVideoSource)
                videoBridge = _localVideoBridge;
            else
                return;

            videoBridge.TryServeVideoFrame(args);
        }
    }

    
}
