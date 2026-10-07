using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MiParte.Web;
using MiParte.Web.Configuracion;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMiParteWeb(OpcionesWeb.Leer(builder.Configuration));

await builder.Build().RunAsync();
