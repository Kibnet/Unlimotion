using System;
using Android.App;
using Android.Content;
using Android.Net;
using Uri = Android.Net.Uri;

namespace Unlimotion.Android;

public class AndroidSafPermission : IAndroidSafPermission
{
    private readonly Activity _activity;
    public AndroidSafPermission(Activity activity) => _activity = activity;

    public void TakePersistableUriPermission(string contentUriString)
    {
        if (string.IsNullOrWhiteSpace(contentUriString)) return;

        var uri = Uri.Parse(contentUriString);

        // Флаги должны совпадать с тем, что вернул SAF. Обычно достаточно READ + WRITE + PERSISTABLE.
        var flags =
            ActivityFlags.GrantReadUriPermission |
            ActivityFlags.GrantWriteUriPermission |
            ActivityFlags.GrantPersistableUriPermission;

        try
        {
            _activity.ContentResolver.TakePersistableUriPermission(uri, flags);
        }
        catch
        {
            // На некоторых девайсах требуется использовать именно флаги из интента результата.
            // Если у вас есть Intent результата выбора — подставьте его флаги вместо констант.
            // Иначе: повторно попросите выбрать папку.
        }
    }
}