using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using ICSharpCode.SharpZipLib.Zip;
using Play.Models;
using ReactiveUI;
using ReactiveUI.Xaml;

namespace Play.ViewModels
{
    public interface ISongTileViewModel : IReactiveNotifyPropertyChanged
    {
        Song Model { get; }
        BitmapImage AlbumArt { get; }
        bool IsStarred { get; set; }
        Visibility QueueSongVisibility { get; set; }

        ReactiveAsyncCommand QueueSong { get; }
        ReactiveAsyncCommand QueueAlbum { get; }

        ReactiveCommand DownloadSong { get; }
        ReactiveCommand DownloadAlbum { get; }

        ReactiveAsyncCommand ShowSongsFromArtist { get; }
        ReactiveAsyncCommand ShowSongsFromAlbum { get; }

        ReactiveAsyncCommand ToggleStarred { get; }
    }

    public class SongTileViewModel : ReactiveObject, ISongTileViewModel
    {
        public Song Model { get; protected set; }

        ObservableAsPropertyHelper<BitmapImage> _AlbumArt;
        public BitmapImage AlbumArt {
            get { return _AlbumArt.Value; }
        }

        bool _IsStarred;
        public bool IsStarred {
            get { return _IsStarred; }
            set { this.RaiseAndSetIfChanged(x => x.IsStarred, value); }
        }

        Visibility _QueueSongVisibility;
        public Visibility QueueSongVisibility {
            get { return _QueueSongVisibility; }
            set { this.RaiseAndSetIfChanged(x => x.QueueSongVisibility, value); }
        }

        public ReactiveAsyncCommand QueueSong { get; protected set; }
        public ReactiveAsyncCommand QueueAlbum { get; protected set; }

        public ReactiveCommand DownloadSong { get; protected set; }
        public ReactiveCommand DownloadAlbum { get; protected set; }

        public ReactiveAsyncCommand ShowSongsFromArtist { get; protected set; }
        public ReactiveAsyncCommand ShowSongsFromAlbum { get; protected set; }

        public ReactiveAsyncCommand ToggleStarred { get; protected set; }

        public SongTileViewModel(Song model, IPlayApi playApi)
        {
            Model = model;

            playApi.FetchImageForAlbum(model)
                .LoggedCatch(this, Observable.Return(default(BitmapImage)))
                .ToProperty(this, x => x.AlbumArt);

            QueueSong = new ReactiveAsyncCommand();
            QueueAlbum = new ReactiveAsyncCommand();

            QueueSong.RegisterAsyncObservable(_ => playApi.QueueSong(Model))
                .Subscribe(
                    x => this.Log().Info("Queued {0}", Model.name),
                    ex => this.Log().WarnException("Failed to queue", ex));

            QueueAlbum.RegisterAsyncObservable(_ => playApi.AllSongsOnAlbum(Model.artist, Model.album))
                .SelectMany(x => x.ToObservable())
                .Select(x => reallyTryToQueueSong(playApi, x)).Concat()
                .Subscribe(
                    x => this.Log().Info("Queued song"),
                    ex => this.Log().WarnException("Failed to queue album", ex));

            QueueAlbum.ThrownExceptions.Subscribe(x => { });

            DownloadAlbum = new ReactiveCommand();

            DownloadSong = new ReactiveCommand();

            IsStarred = model.starred;
            ToggleStarred = new ReactiveAsyncCommand();

            ToggleStarred.RegisterAsyncObservable(_ => IsStarred ? playApi.Unstar(Model) : playApi.Star(Model))
                .Select(_ => true).LoggedCatch(this, Observable.Return(false))
                .Subscribe(result => {
                    if (result) IsStarred = !IsStarred;
                }, ex => this.Log().WarnException("Couldn't star/unstar song", ex));

            ToggleStarred.ThrownExceptions.Subscribe(x => { });
        }

        IObservable<Unit> reallyTryToQueueSong(IPlayApi playApi, Song song)
        {
            return Observable.Defer(() => playApi.QueueSong(song))
                .Timeout(TimeSpan.FromSeconds(20), RxApp.TaskpoolScheduler)
                .Retry(3);
        }

        IObservable<Unit> postProcessSong(Tuple<string, byte[]> fileAndData, string rootPath = null)
        {
            rootPath = rootPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var paths = new[] {
                Model.artist, Model.album
            };

            return Observable.Start(() => {
                var targetDir = paths.Aggregate(new DirectoryInfo(rootPath), (acc, x) => {
                    if (!acc.Exists) {
                        acc.Create();
                    }
                    return new DirectoryInfo(Path.Combine(acc.FullName, x));
                });

                File.WriteAllBytes(Path.Combine(targetDir.FullName, fileAndData.Item1), fileAndData.Item2);
            }, RxApp.TaskpoolScheduler);
        }

        IObservable<Unit> postProcessAlbum(Tuple<string, byte[]> fileAndData, string rootPath = null)
        {
            rootPath = rootPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var paths = new[] {
                Model.artist, Model.album
            };

            return Observable.Start(() => {
                var targetDir = paths.Aggregate(new DirectoryInfo(rootPath), (acc, x) => {
                    if (!acc.Exists) {
                        acc.Create();
                    }
                    return new DirectoryInfo(Path.Combine(acc.FullName, x));
                });

                var zip = new FastZip();
                zip.ExtractZip(new MemoryStream(fileAndData.Item2), targetDir.FullName, FastZip.Overwrite.Never, null, null, null, true, true);
            }, RxApp.TaskpoolScheduler);
        }
    }
}