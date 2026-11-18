using System.Text.Json;
using System.Text.Json.Serialization;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Services;

/// <summary>Serialización generada en compilación: funciona igual con el recorte (trimming) de Release.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(CapatazDto))]
[JsonSerializable(typeof(ObraDetalleDto))]
[JsonSerializable(typeof(NuevoRegistroRequest))]
[JsonSerializable(typeof(CambiarEtapaRequest))]
[JsonSerializable(typeof(ValidacionesRequest))]
[JsonSerializable(typeof(AtrasoRequest))]
[JsonSerializable(typeof(NuevoGastoRequest))]
[JsonSerializable(typeof(NuevoSobranteRequest))]
[JsonSerializable(typeof(NotaPedidoRequest))]
[JsonSerializable(typeof(LeidoRequest))]
[JsonSerializable(typeof(ErrorApp))]
[JsonSerializable(typeof(List<Envio>))]
internal partial class AppJson : JsonSerializerContext;
