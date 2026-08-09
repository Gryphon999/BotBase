FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BotBase.Api/BotBase.Api.csproj BotBase.Api/
COPY BotBase.BlazorUI/BotBase.BlazorUI.csproj BotBase.BlazorUI/
RUN dotnet restore BotBase.Api/BotBase.Api.csproj

COPY . .

RUN dotnet publish BotBase.Api/BotBase.Api.csproj -c Release -o /api-out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /api-out .

ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_HTTP_PORTS=$PORT dotnet BotBase.Api.dll"]
