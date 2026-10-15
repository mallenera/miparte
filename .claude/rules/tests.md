---
paths:
  - "**/*Tests/**"
  - "**/*Tests.cs"
---
# Tests

- xUnit; bUnit para componentes Blazor. Nombres de test descriptivos en español.
- Tests con BD: `[SkippableFact]` condicionados a `MIPARTE_TEST_DB`; CI siempre los ejecuta (postgres:17). Si tocas esquema o RLS, ejecútalos en local con la BD real.
- Ejecuta `dotnet test` (o `--filter "FullyQualifiedName~Clase"`) antes de dar algo por terminado e informa de los fallos tal cual, sin maquillarlos.
- No dependas de la fecha actual ni del orden de ejecución; datos de prueba propios por test.
- Una regla de negocio nueva se prueba en su capa: función pura del dominio, o caso de uso con un repositorio en memoria (`Core.Tests/Servicio*Tests.cs`, sin HTTP ni BD) y un `TimeProvider` manual si depende del reloj. Los tests HTTP solo comprueban el cableado y el código de estado.
