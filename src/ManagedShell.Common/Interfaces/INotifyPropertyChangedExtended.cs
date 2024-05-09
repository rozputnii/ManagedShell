

// ReSharper disable once CheckNamespace
namespace System.ComponentModel;

public interface INotifyPropertyChangedExtended : INotifyPropertyChanged
{
	void InvokePropertyChanged(PropertyChangedEventArgs e);
}