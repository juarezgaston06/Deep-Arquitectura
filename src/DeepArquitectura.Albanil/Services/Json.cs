using System.Text.Json;
using System.Text.Json.Serialization;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Services;

/// <summary>Serialización generada en compilación: funciona igual con el recorte (trimming) de Release.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginAlbanilResponse))]
[JsonSerializable(typeof(AlbanilDto))]
[JsonSerializable(typeof(ObraAlbanilDto))]
[JsonSerializable(typeof(NuevoAvanceRequest))]
[JsonSerializable(typeof(NuevoAvanceResponse))]
[JsonSerializable(typeof(FichadaRequest))]
[JsonSerializable(typeof(NuevoPedidoRequest))]
[JsonSerializable(typeof(NuevoProblemaRequest))]
[JsonSerializable(typeof(ParteRequest))]
[JsonSerializable(typeof(NuevaRecepcionRequest))]
[JsonSerializable(typeof(RespuestaControlRequest))]
[JsonSerializable(typeof(NotaPedidoRequest))]
[JsonSerializable(typeof(LeidoRequest))]
[JsonSerializable(typeof(IdResponse))]
[JsonSerializable(typeof(ErrorApp))]
[JsonSerializable(typeof(List<Envio>))]
internal partial class AppJson : JsonSerializerContext;
