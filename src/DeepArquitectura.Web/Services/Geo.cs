namespace DeepArquitectura.Web.Services;

public static class Geo
{
    /// <summary>Distancia en metros entre dos puntos (haversine). Null si falta alguno.</summary>
    public static int? DistanciaMetros(double? lat1, double? lon1, double? lat2, double? lon2)
    {
        if (lat1 is null || lon1 is null || lat2 is null || lon2 is null) return null;
        const double radioTierra = 6_371_000;
        static double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2.Value - lat1.Value);
        var dLon = Rad(lon2.Value - lon1.Value);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(lat1.Value)) * Math.Cos(Rad(lat2.Value)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return (int)Math.Round(radioTierra * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }

    /// <summary>«350 m» o «2,4 km».</summary>
    public static string Texto(int metros) =>
        metros < 1000 ? $"{metros} m" : (metros / 1000.0).ToString("0.#", new System.Globalization.CultureInfo("es-AR")) + " km";

    /// <summary>
    /// Ubicación de la obra estimada a partir de las fichadas: la mediana de latitud y longitud (un par de fichadas
    /// hechas desde otro lado no la mueven). Hace falta al menos 3 puntos con buena precisión.
    /// </summary>
    public static (double Lat, double Lon)? Mediana(IReadOnlyCollection<(double Lat, double Lon)> puntos)
    {
        if (puntos.Count < 3) return null;
        static double Med(IEnumerable<double> v)
        {
            var o = v.Order().ToArray();
            return o.Length % 2 == 1 ? o[o.Length / 2] : (o[o.Length / 2 - 1] + o[o.Length / 2]) / 2;
        }
        return (Med(puntos.Select(p => p.Lat)), Med(puntos.Select(p => p.Lon)));
    }
}
