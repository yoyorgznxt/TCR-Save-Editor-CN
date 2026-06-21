using System.Collections.ObjectModel;
using System.ComponentModel;

namespace TCRSaveEditor.Models
{
    public class City : INotifyPropertyChanged
    {
        private string _mapName = string.Empty;
        private string _faction = string.Empty;
        private int _peoplesCount;
        private int _reservistsCount;

        public int Index { get; set; }
        public long PeoplesCountOffset { get; set; }
        public long ReservistsCountOffset { get; set; }

        public string MapName
        {
            get => _mapName;
            set { _mapName = value; OnPropertyChanged(nameof(MapName)); }
        }

        public string Faction
        {
            get => _faction;
            set { _faction = value; OnPropertyChanged(nameof(Faction)); }
        }

        public int PeoplesCount
        {
            get => _peoplesCount;
            set { _peoplesCount = value; OnPropertyChanged(nameof(PeoplesCount)); }
        }

        public int ReservistsCount
        {
            get => _reservistsCount;
            set { _reservistsCount = value; OnPropertyChanged(nameof(ReservistsCount)); }
        }

        public ObservableCollection<Resource> Resources { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}