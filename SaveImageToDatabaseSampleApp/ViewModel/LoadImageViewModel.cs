﻿using System;
using System.Net;
using System.Linq;
using System.Net.Http;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Collections.Generic;

using Xamarin.Forms;

using SaveImageToDatabaseSampleApp.Shared;

namespace SaveImageToDatabaseSampleApp
{
    public class LoadImageViewModel : BaseViewModel
    {
        #region Constant Fields
        const int _downloadImageTimeoutInSeconds = 15;
        #endregion

        #region Fields
        bool _isImageDownloading, _isImageVisible, _isLoadImageButtonEnabled;
        string _imageUrlEntryText = @"https://xamcamstorage.blob.core.windows.net/asset-e5f64394-98b0-4304-8a7a-1e4158e658c2/15%20Yellow%20Submarine.mp3?sv=2015-07-08&sr=c&si=9dd76733-1f63-4f28-8cbc-2f4a0755d466&sig=OWgmzC3W2mqImg40fxtPrPO7As5U0x2Y9XtRM96DI20%3D&st=2017-08-15T23%3A20%3A23Z&se=2117-08-15T23%3A20%3A23Z";
        string _downloadImageButtonText;
        ImageSource _downloadedImageSource;
        Lazy<HttpClient> _clientHolder = new Lazy<HttpClient>(CreateHttpClient);
        ICommand _loadImageButtonTapped, _clearImageButtonTapped;
        List<DownloadedImageModel> _imageDatabaseModelList;
        #endregion

        #region Constructors
        public LoadImageViewModel()
        {
            Task.Run(async () =>
            {
                await RefreshDataAsync().ConfigureAwait(false);
                await UpdateDownloadButtonText().ConfigureAwait(false);
            });
        }
        #endregion

        #region Events
        public event EventHandler<string> ImageDownloadFailed;
        #endregion

        #region Properties
        public ICommand LoadImageButtonTapped => _loadImageButtonTapped ??
            (_loadImageButtonTapped = new Command(async () => await ExecuteLoadImageButtonTappedAsync()));

        public ICommand ClearImageButtonTapped => _clearImageButtonTapped ??
            (_clearImageButtonTapped = new Command(ExecuteClearImageButtonTapped));

        public bool IsImageDownloading
        {
            get => _isImageDownloading;
            set => SetProperty(ref _isImageDownloading, value);
        }

        public bool AreImageAndClearButtonVisible
        {
            get => _isImageVisible;
            set => SetProperty(ref _isImageVisible, value);
        }

        public string ImageUrlEntryText
        {
            get => _imageUrlEntryText;
            set => SetProperty(ref _imageUrlEntryText, value, async () => await UpdateDownloadButtonText());
        }

        public ImageSource DownloadedImageSource
        {
            get => _downloadedImageSource;
            set => SetProperty(ref _downloadedImageSource, value);
        }

        public string DownloadImageButtonText
        {
            get => _downloadImageButtonText;
            set => SetProperty(ref _downloadImageButtonText, value);
        }

        public bool IsLoadImageButtonEnabled
        {
            get => _isLoadImageButtonEnabled;
            set => SetProperty(ref _isLoadImageButtonEnabled, value);
        }

        public List<DownloadedImageModel> DownloadedImageModelList
        {
            get => _imageDatabaseModelList;
            set => SetProperty(ref _imageDatabaseModelList, value);
        }

        HttpClient Client => _clientHolder.Value;
		#endregion

		#region Methods
		static HttpClient CreateHttpClient()
		{
			var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip })
			{
				Timeout = TimeSpan.FromSeconds(_downloadImageTimeoutInSeconds)
			};
			client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

			return client;
		}

        async Task ExecuteLoadImageButtonTappedAsync()
        {
            if (DownloadImageButtonText.Equals(LoadImageButtonTextConstants.LoadImageFromDatabaseButtonText))
                await LoadImageFromDatabaseAsync(ImageUrlEntryText);
            else
                await DownloadImageAsync(ImageUrlEntryText);
        }

        void ExecuteClearImageButtonTapped(object obj)
        {
            AnalyticsHelpers.TrackEvent(AnalyticsConstants.ClearButtonTapped);

            AreImageAndClearButtonVisible = false;
        }

        async Task UpdateDownloadButtonText()
        {
            await RefreshDataAsync().ConfigureAwait(false);

            if (IsUrInDatabase(ImageUrlEntryText))
                DownloadImageButtonText = LoadImageButtonTextConstants.LoadImageFromDatabaseButtonText;
            else
                DownloadImageButtonText = LoadImageButtonTextConstants.DownloadImageFromUrlButtonText;

            IsLoadImageButtonEnabled = true;
        }

        async Task LoadImageFromDatabaseAsync(string imageUrl)
        {
            AnalyticsHelpers.TrackEvent(AnalyticsConstants.LoadImageFromDatabase, new Dictionary<string, string>
            {
                { AnalyticsConstants.ImageUrl, imageUrl }
            });

            var downloadedImageModel = await DownloadedImageModelDatabase.GetDownloadedImageAsync(imageUrl).ConfigureAwait(false);

            DownloadedImageSource = downloadedImageModel.DownloadedImageAsImageStream;

            AreImageAndClearButtonVisible = true;
        }

        async Task DownloadImageAsync(string imageUrl)
        {
            byte[] downloadedImage;

            SetIsImageDownloading(true);

            try
            {
                using (var httpResponse = await Client.GetAsync(imageUrl).ConfigureAwait(false))
                {
                    if (httpResponse.StatusCode == HttpStatusCode.OK)
                    {
                        downloadedImage = await httpResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                        var downloadedImageModel = new DownloadedImageModel
                        {
                            ImageUrl = imageUrl,
                            DownloadedImageBlob = downloadedImage
                        };

                        await DownloadedImageModelDatabase.SaveDownloadedImage(downloadedImageModel).ConfigureAwait(false);

                        DownloadedImageSource = downloadedImageModel.DownloadedImageAsImageStream;
                        AreImageAndClearButtonVisible = true;

                        AnalyticsHelpers.TrackEvent(AnalyticsConstants.DownloadImage, new Dictionary<string, string>
                        {
                            { AnalyticsConstants.ImageDownloadSuccessful, imageUrl }
                        });
                    }
                    else
                    {
                        AnalyticsHelpers.TrackEvent(AnalyticsConstants.DownloadImage, new Dictionary<string, string>
                        {
                            { AnalyticsConstants.ImageDownloadFailed, imageUrl }
                        });
                        OnImageDownloadFailed("Invalid Url");
                    }
                }
            }
            catch (Exception e)
            {
                AnalyticsHelpers.TrackEvent(AnalyticsConstants.DownloadImage, new Dictionary<string, string>
                {
                    { AnalyticsConstants.ImageDownloadFailed, imageUrl }
                });
                OnImageDownloadFailed(e.Message);
            }
            finally
            {
                SetIsImageDownloading(false);
                await UpdateDownloadButtonText();
            }
        }

        void SetIsImageDownloading(bool isImageDownloading)
        {
            IsImageDownloading = isImageDownloading;
            IsLoadImageButtonEnabled = !isImageDownloading;
        }

        bool IsUrInDatabase(string url) =>
            DownloadedImageModelList.Any(x => x.ImageUrl.ToUpper().Equals(url.ToUpper()));

        async Task RefreshDataAsync() =>
            DownloadedImageModelList = await DownloadedImageModelDatabase.GetAllDownloadedImagesAsync();

        void OnImageDownloadFailed(string failureMessage) =>
            ImageDownloadFailed?.Invoke(this, failureMessage);
        #endregion
    }
}
