using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ManagedShell.Common.Extensions;

public static class NotifyPropertyChangedExtensions
{
	public static bool SetProperty<T>(this INotifyPropertyChangedExtended obj,
		[NotNullIfNotNull(nameof(newValue))] ref T field, T newValue,
		[CallerMemberName] string propertyName = null,
		Action<T> callback = null
	)
	{
		if (EqualityComparer<T>.Default.Equals(field, newValue)) return false;

		field = newValue;
		obj.OnPropertyChanged(propertyName);
		callback?.Invoke(newValue);

		return true;
	}

	public static bool SetProperty<T>(
		this INotifyPropertyChangedExtended obj,
		ref T field, ref T newValue,
		[CallerMemberName] string propertyName = null,
		Action<T> callback = null
	) where T : struct
	{
		if (EqualityComparer<T>.Default.Equals(field, newValue)) return false;

		field = newValue;
		obj.OnPropertyChanged(propertyName);
		callback?.Invoke(newValue);

		return true;
	}

	public static bool SetProperty<T>(
		this INotifyPropertyChangedExtended obj,
		[NotNullIfNotNull(nameof(newValue))] ref T? field, ref T? newValue,
		[CallerMemberName] string propertyName = null,
		Action<T?> callback = null
	) where T : struct
	{
		if (EqualityComparer<T?>.Default.Equals(field, newValue)) return false;

		field = newValue;
		obj.OnPropertyChanged(propertyName);
		callback?.Invoke(newValue);

		return true;
	}

	public static bool SetProperty<T>(
		this INotifyPropertyChangedExtended obj,
		[NotNullIfNotNull(nameof(newValue))] ref T? field, ref T newValue,
		[CallerMemberName] string propertyName = null,
		Action<T> callback = null
	) where T : struct
	{
		if (field.HasValue && EqualityComparer<T>.Default.Equals(field.Value, newValue)) return false;

		field = newValue;
		obj.OnPropertyChanged(propertyName);
		callback?.Invoke(newValue);

		return true;
	}

	private static void OnPropertyChanged(this INotifyPropertyChangedExtended obj, string propertyName)
	{
		if (propertyName is null) return;
		obj.InvokePropertyChanged(new PropertyChangedEventArgs(propertyName));
	}
}