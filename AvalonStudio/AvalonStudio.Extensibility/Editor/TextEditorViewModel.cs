﻿using Avalonia.Input;
using AvalonStudio.Controls;
using AvalonStudio.Documents;
using AvalonStudio.Platforms;
using AvalonStudio.Projects;
using AvalonStudio.Shell;
using ReactiveUI;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace AvalonStudio.Extensibility.Editor
{
    public class TextEditorViewModel : EditorViewModel
    {
        private string _zoomLevelText;
        private double _fontSize;
        private double _zoomLevel;
        private double _visualFontSize;
        private IShell _shell;
        private string _sourceText;
        private IEditor _documentAccessor;
        private CompositeDisposable _disposables;
        private bool _isReadOnly;

        public TextEditorViewModel(ISourceFile file) : base(file)
        {
            _shell = IoC.Get<IShell>();
            _visualFontSize = _fontSize = 14;
            _zoomLevel = 1;
            Title = file.Name;

            this.WhenAnyValue(x => x.DocumentAccessor).Subscribe(accessor =>
            {
                _disposables?.Dispose();
                _disposables = null;

                if (accessor != null)
                {
                    _disposables = new CompositeDisposable
                    {
                        Observable.FromEventPattern<TextInputEventArgs>(accessor, nameof(accessor.TextEntered)).Subscribe(args =>
                        {
                            IsDirty = true;
                        }),
                        Observable.FromEventPattern<TooltipDataRequestEventArgs>(accessor, nameof(accessor.RequestTooltipContent)).Subscribe(args =>
                        {

                        })
                    };
                }
            });
            //ZoomLevel = _shell.GlobalZoomLevel;
        }        

        public bool IsReadOnly
        {
            get { return _isReadOnly; }
            set { this.RaiseAndSetIfChanged(ref _isReadOnly, value); }
        }

        public override void Close()
        {
            base.Close();

            _disposables.Dispose();
        }

        ~TextEditorViewModel()
        {
        }

        public string SourceText
        {
            get { return _sourceText; }
            set { this.RaiseAndSetIfChanged(ref _sourceText, value); }
        }

        public string FontFamily
        {
            get
            {
                switch (Platform.PlatformIdentifier)
                {
                    /*case Platforms.PlatformID.Win32NT:
                        return "Consolas";*/

                    default:
                        return "Source Code Pro";
                }
            }
        }

        public double FontSize
        {
            get
            {
                return _fontSize;
            }
            set
            {
                if (_fontSize != value)
                {
                    _fontSize = value;
                    InvalidateVisualFontSize();
                }
            }
        }

        public double ZoomLevel
        {
            get
            {
                return _zoomLevel;
            }
            set
            {
                if (value != _zoomLevel)
                {
                    _zoomLevel = value;
                    //_shell.GlobalZoomLevel = value;
                    InvalidateVisualFontSize();

                    ZoomLevelText = $"{ZoomLevel:0} %";
                }
            }
        }

        public string ZoomLevelText
        {
            get { return _zoomLevelText; }
            set { this.RaiseAndSetIfChanged(ref _zoomLevelText, value); }
        }

        public double VisualFontSize
        {
            get { return _visualFontSize; }
            set { this.RaiseAndSetIfChanged(ref _visualFontSize, value); }
        }

        private void InvalidateVisualFontSize()
        {
            VisualFontSize = (ZoomLevel / 100) * FontSize;
        }

        public override async Task WaitForEditorToLoadAsync()
        {
            if (_documentAccessor == null)
            {
                await Task.Run(() =>
                {
                    while (_documentAccessor == null)
                    {
                        Task.Delay(50);
                    }
                });
            }
        }

        public IEditor DocumentAccessor
        {
            get { return _documentAccessor; }
            set { this.RaiseAndSetIfChanged(ref _documentAccessor, value); }
        }

        public override IEditor Editor => _documentAccessor;
    }
}
