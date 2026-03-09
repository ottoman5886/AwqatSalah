FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DiyanetNamazVakti.Api/DiyanetNamazVakti.Api.csproj DiyanetNamazVakti.Api/
RUN dotnet restore DiyanetNamazVakti.Api/DiyanetNamazVakti.Api.csproj

COPY . .
RUN dotnet publish DiyanetNamazVakti.Api/DiyanetNamazVakti.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:TreatWarningsAsErrors=false \
    /p:Nullable=disable

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000

ENTRYPOINT ["dotnet", "DiyanetNamazVakti.Api.dll"]
