using HMUI;
using TMPro;
using System;
using Zenject;
using SiraUtil;
using System.IO;
using UnityEngine;
using SiraUtil.Tools;
using DiTails.Managers;
using System.Threading;
using System.Reflection;
using DiTails.Utilities;
using System.ComponentModel;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaverSharp;

namespace DiTails.UI
{
    internal class DetailViewHost : INotifyPropertyChanged, IInitializable, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _didParse;
        private bool _didSetupVote;
        private Beatmap? _activeBeatSaverMap;
        private CancellationTokenSource _cts;
        private IDifficultyBeatmap? _activeBeatmap;

        private string? _bsmlContent;
        private readonly SiraLog _siraLog;
        private readonly LevelDataService _levelDataService;
        private readonly DetailContextManager _detailContextManager;

        #region Initialization 

        public DetailViewHost(SiraLog siraLog, LevelDataService levelDataService, DetailContextManager detailContextManager)
        {
            _siraLog = siraLog;
            _levelDataService = levelDataService;
            _detailContextManager = detailContextManager;

            _cts = new CancellationTokenSource();
        }

        public void Initialize()
        {
            _detailContextManager.BeatmapUnselected += HideMenu;
            _detailContextManager.DetailMenuRequested += MenuRequested;
        }

        public void Dispose()
        {
            _detailContextManager.BeatmapUnselected -= HideMenu;
            _detailContextManager.DetailMenuRequested -= MenuRequested;
        }

        private async Task Parse(StandardLevelDetailViewController standardLevelDetailViewController)
        {
            if (!_didParse)
            {
                _siraLog.Debug("Doing Initial BSML Parsing of the Detail View");
                _siraLog.Debug("Getting Manifest Stream");
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DiTails.Views.detail-view.bsml"))
                using (var reader = new StreamReader(stream))
                {
                    _siraLog.Debug("Reading Manifest Stream");
                    _bsmlContent = await reader.ReadToEndAsync();
                }
                if (!string.IsNullOrWhiteSpace(_bsmlContent))
                {
                    _siraLog.Debug("Parsing Details");
                    BSMLParser.instance.Parse(_bsmlContent, standardLevelDetailViewController.gameObject, this);
                    if (rootTransform != null && mainModalTransform != null)
                    {
                        rootTransform.gameObject.name = "DiTailsDetailView";
                        mainModalTransform.gameObject.name = "DiTailsMainModal";
                    }
                    if (descriptionModalTransform != null && artworkModalTransform != null)
                    {
                        descriptionModalTransform.gameObject.name = "DiTailsDescriptionModal";
                        artworkModalTransform.gameObject.name = "DiTailsArtworkModal";
                    }
                    _siraLog.Debug("Parsing Complete");
                    _didParse = true;
                }
            }
        }

        private void SetupVotingButtons()
        {
            if (!_didSetupVote)
            {
                if (votingUpvoteImage != null && votingDownvoteImage != null)
                {
                    votingUpvoteImage.SetImage("DiTails.Resources.arrow.png");
                    votingDownvoteImage.SetImage("DiTails.Resources.arrow.png");
                    votingUpvoteImage.DefaultColor = new Color(0.388f, 1f, 0.388f);
                    votingDownvoteImage.DefaultColor = new Color(1f, 0.188f, 0.188f);

                    votingUpvoteImage.transform.localScale = new Vector2(0.9f, 1f);
                    votingDownvoteImage.transform.localScale = new Vector2(0.9f, -1f);

                    _didSetupVote = true;
                }
            }
        }

        #endregion

        #region Callbacks

        private void HideMenu()
        {
            _cts.Cancel();
            if (_didParse && rootTransform != null && mainModalTransform != null && descriptionModalTransform != null && artworkModalTransform != null)
            {
                mainModalTransform.transform.SetParent(rootTransform.transform);
                descriptionModalTransform.transform.SetParent(rootTransform.transform);
                artworkModalTransform.transform.SetParent(rootTransform.transform);
            }
            parserParams?.EmitEvent("hide");
        }

        private void MenuRequested(StandardLevelDetailViewController standardLevelDetailViewController, IDifficultyBeatmap difficultyBeatmap)
        {
            _cts = new CancellationTokenSource();
            _ = LoadMenu(standardLevelDetailViewController, difficultyBeatmap);
        }

        #endregion

        #region Usage

        private async Task LoadMenu(StandardLevelDetailViewController standardLevelDetailViewController, IDifficultyBeatmap difficultyBeatmap)
        {
            _activeBeatmap = difficultyBeatmap;
            await Parse(standardLevelDetailViewController);
            SetupVotingButtons();

            ShowPanel = false;
            parserParams?.EmitEvent("show-detail");
            var map = await _levelDataService.GetBeatmap(difficultyBeatmap, _cts.Token);
            ShowPanel = true;
            if (map != null)
            {
                Key = map.Key;
                Author = difficultyBeatmap.level.songAuthorName;
                Mapper = difficultyBeatmap.level.levelAuthorName ?? map.Uploader.Username ?? "Unknown";
                Uploaded = map.Uploaded.ToString("MMMM dd, yyyy");
                Downloads = map.Stats.Downloads.ToString();
                Votes = (map.Stats.UpVotes + -map.Stats.DownVotes).ToString();
                SetRating(map.Stats.Rating);
            }
            _activeBeatSaverMap = map;
        }

        private void SetRating(float value)
        {
            if (rating != null)
            {
                rating.text = string.Format("{0:0%}", value);
                rating.color = Constants.Evaluate(value);
            }
        }

        #endregion

        #region BSML Actions

        [UIAction("view-description")]
        protected async Task ViewDescription()
        {
            parserParams?.EmitEvent("hide");
            if (_activeBeatSaverMap != null && textPageScrollView != null)
            {
                textPageScrollView.SetText(_activeBeatSaverMap.Description ?? "DITAILS_NODESCRIPTION".LocalizationGetOr("No Description"));
                textPageScrollView.SetDestinationPosY(0);
                textPageScrollView.ScrollTo(0, false);
                textPageScrollView.RefreshButtons();
            }
            await SiraUtil.Utilities.PauseChamp;
            parserParams?.EmitEvent("show-description");
        }

        [UIAction("close-description")]
        protected async Task CloseDescription()
        {
            parserParams?.EmitEvent("hide");
            await SiraUtil.Utilities.PauseChamp;
            parserParams?.EmitEvent("show-detail");
        }

        [UIAction("view-artwork")]
        protected async Task ViewArtwork()
        {
            parserParams?.EmitEvent("hide");
            if (artworkImage != null && _activeBeatmap != null)
            {
                artworkImage.sprite = await _activeBeatmap.level.GetCoverImageAsync(_cts.Token);
            }
            await SiraUtil.Utilities.PauseChamp;
            parserParams?.EmitEvent("show-artwork");
        }

        [UIAction("close-artwork")]
        protected async Task CloseArtwork()
        {
            parserParams?.EmitEvent("hide");
            await SiraUtil.Utilities.PauseChamp;
            parserParams?.EmitEvent("show-detail");
        }

        #endregion

        #region BSML Bindings

        [UIValue("show-loading")]
        public bool ShowLoading => !ShowPanel;

        private bool _showPanel = false;
        [UIValue("show-panel")]
        protected bool ShowPanel
        {
            get => _showPanel;
            set
            {
                _showPanel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowPanel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLoading)));
            }
        }

        private string _key = "Key | 0";
        [UIValue("key")]
        protected string Key
        {
            get => _key;
            set
            {
                _key = string.Join(" | ", "DITAILS_KEY".LocalizationGetOr("Key"), value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Key)));
            }
        }

        private string _author = "Author | Unknown";
        [UIValue("author")]
        protected string Author
        {
            get => _author;
            set
            {
                _author = string.Join(" | ", "DITAILS_AUTHOR".LocalizationGetOr("Author"), value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Author)));
            }
        }

        private string _mapper = "Mapper | None";
        [UIValue("mapper")]
        protected string Mapper
        {
            get => _mapper;
            set
            {
                _mapper = string.Join(" | ", "DITAILS_MAPPER".LocalizationGetOr("Mapper"), value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mapper)));
            }
        }

        private string _uploaded = "Uploaded | Never";
        [UIValue("uploaded")]
        protected string Uploaded
        {
            get => _uploaded;
            set
            {
                _uploaded = string.Join(" | ", "DITAILS_UPLOADED".LocalizationGetOr("Uploaded"), value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Uploaded)));
            }
        }

        private string _downloads = "Downloads | 0";
        [UIValue("downloads")]
        protected string Downloads
        {
            get => _downloads;
            set
            {
                _downloads = string.Join(" | ", "DITAILS_DOWNLOADS".LocalizationGetOr("Downloads"), value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Downloads)));
            }
        }

        private string _votes = "0";
        [UIValue("votes")]
        protected string Votes
        {
            get => _votes;
            set
            {
                _votes = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Votes)));
            }
        }

        private string _description = "DITAILS_NODESCRIPTION".LocalizationGetOr("No Description");
        [UIValue("description")]
        protected string Description
        {
            get => _description;
            set
            {
                _description = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            }
        }

        #endregion

        #region BSML Variables

        [UIParams]
        protected BSMLParserParams? parserParams;

        [UIComponent("rating")]
        protected TextMeshProUGUI? rating;

        [UIComponent("artwork-image")]
        protected ImageView? artworkImage;

        [UIComponent("root")]
        protected RectTransform? rootTransform;

        [UIComponent("main-modal")]
        protected RectTransform? mainModalTransform;

        [UIComponent("description-scroller")]
        protected TextPageScrollView? textPageScrollView;

        [UIComponent("description-modal")]
        protected RectTransform? descriptionModalTransform;

        [UIComponent("artwork-modal")]
        protected RectTransform? artworkModalTransform;

        [UIComponent("voting-upvote-image")]
        protected ClickableImage? votingUpvoteImage;

        [UIComponent("voting-downvote-image")]
        protected ClickableImage? votingDownvoteImage;

        #endregion
    }
}