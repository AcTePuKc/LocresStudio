using System.ComponentModel;

namespace UnrealLocresEditor.Models
{
    public class DataRow : INotifyPropertyChanged
    {
        private string[] _values;
        public string[] Values
        {
            get => _values;
            set
            {
                if (_values != value)
                {
                    _values = value;
                    OnPropertyChanged(nameof(Values));
                }
            }
        }

        public bool IsNewKey { get; set; }

        public uint SourceHash { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

       
        public DataRow()
        {
            _values = System.Array.Empty<string>();
        }

        public virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
