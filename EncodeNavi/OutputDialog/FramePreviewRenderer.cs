using System.Windows.Media.Imaging;
using YukkuriMovieMaker.ItemEditor;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Plugin;

namespace EncodeNavi.OutputDialog
{
    internal sealed class FramePreviewRenderer : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

        private readonly Scene scene;
        private readonly Action<BitmapSource> onRendered;
        private readonly AutoResetEvent requested = new(false);
        private readonly ManualResetEvent disposed = new(false);
        private readonly Thread thread;
        private readonly Lock gate = new();
        private volatile int requestedFrame = -1;
        private bool isStopped;

        public FramePreviewRenderer(Scene scene, Action<BitmapSource> onRendered)
        {
            this.scene = scene;
            this.onRendered = onRendered;
            thread = new Thread(Run) { IsBackground = true, Name = "EncodeNavi preview" };
            thread.Start();
        }

        public void Request(int frame)
        {
            requestedFrame = frame;
            lock (gate)
            {
                if (!isStopped)
                    requested.Set();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (!isStopped)
                    disposed.Set();
            }
        }

        private void Run()
        {
            try
            {
                using var source = new TimelineSourceAndDevices(scene);
                var lastFrame = -1;
                var lastRendered = DateTime.MinValue;
                var events = new WaitHandle[] { disposed, requested };
                while (WaitHandle.WaitAny(events) == 1)
                {
                    var wait = lastRendered + Interval - DateTime.Now;
                    if (wait > TimeSpan.Zero && disposed.WaitOne(wait))
                        return;

                    var frame = requestedFrame;
                    if (frame < 0 || frame == lastFrame)
                        continue;

                    source.Update(scene.Timeline.VideoInfo.GetTimeFrom(frame), TimelineSourceUsage.Exporting);
                    var bitmap = source.RenderBitmapSource();
                    if (bitmap.CanFreeze)
                        bitmap.Freeze();
                    lastFrame = frame;
                    lastRendered = DateTime.Now;
                    onRendered(bitmap);
                }
            }
            catch (Exception ex)
            {
                YukkuriMovieMaker.Commons.Log.Default.Write("EncodeNavi: 出力中の映像の表示に失敗しました: " + ex.Message);
            }
            finally
            {
                lock (gate)
                {
                    isStopped = true;
                    requested.Dispose();
                    disposed.Dispose();
                }
            }
        }
    }
}
