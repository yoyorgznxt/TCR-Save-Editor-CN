using System.ComponentModel;

namespace TCRSaveEditor.Models
{
    public class GameMeta : INotifyPropertyChanged
    {
        private float _politicPoints;
        private float _authorityPoints;
        private float _stabilityPoints;
        private float _dictatorShip;

        public long PoliticPointsOffset { get; set; }
        public long AuthorityPointsOffset { get; set; }
        public long StabilityPointsOffset { get; set; }
        public long DictatorShipOffset { get; set; }

        public float PoliticPoints
        {
            get => _politicPoints;
            set { _politicPoints = value; OnPropertyChanged(nameof(PoliticPoints)); }
        }
        public float AuthorityPoints
        {
            get => _authorityPoints;
            set { _authorityPoints = value; OnPropertyChanged(nameof(AuthorityPoints)); }
        }
        public float StabilityPoints
        {
            get => _stabilityPoints;
            set { _stabilityPoints = value; OnPropertyChanged(nameof(StabilityPoints)); }
        }
        public float DictatorShip
        {
            get => _dictatorShip;
            set { _dictatorShip = value; OnPropertyChanged(nameof(DictatorShip)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}