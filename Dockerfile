FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BotBase.Api/BotBase.Api.csproj BotBase.Api/
COPY BotBase.BlazorUI/BotBase.BlazorUI.csproj BotBase.BlazorUI/
RUN dotnet restore BotBase.Api/BotBase.Api.csproj
RUN dotnet restore BotBase.BlazorUI/BotBase.BlazorUI.csproj

COPY . .

RUN dotnet publish BotBase.BlazorUI/BotBase.BlazorUI.csproj -c Release -o /blazor-out
RUN dotnet publish BotBase.Api/BotBase.Api.csproj -c Release -o /api-out

RUN cp -r /blazor-out/wwwroot/. /api-out/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /api-out .

ENV ASPNETCORE_URLS=http://+:$PORT
ENV PORT=8080

ENTRYPOINT ["dotnet", "BotBase.Api.dll"]
