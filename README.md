# シェアサイクル ステーションののモニタリング

ArcGIS Maps SDK for .NET を使用して、シェアサイクル ステーションの自転車の貸出状況を表示する .NET MAUI アプリケーションです。

このアプリは、カスタム DynamicEntityDataSource を使用して、[公共交通オープンデータで公開されているシェアサイクル ステーションのデータ](https://ckan.odpt.org/dataset/c_bikeshare_gbfs-openstreet)を定期的にポーリングして、自転車の貸出状況の更新を確認します。
<!---
詳細については、「[.NET MAUI で作成するリアルタイム アプリ（シェアサイクルのモニタリング）](xxx)」のブログ記事を参照してください。
--->
<img src="bike-app-maui.png" width="500">


## アプリを使用する方法:
1. お使いの環境が ArcGIS Maps SDK for .NET の[システム要件]((https://developers.arcgis.com/net/reference/system-requirements/))を満たしていることを確認してください。
    - ArcGIS Maps SDK for .NET バージョン 200.2 で動作確認をしています。
1. このリポジトリのクローンをローカル マシンに作成します。
1. Visual Studio プロジェクト (BikeAvailability.csproj) を開きます。
   - ArcGIS Maps SDK for .NET を含む、必要な NuGet パッケージが復元されます。
1. MauiProgram.cs の 37 行目に API キーを追加します。
   - API キーを作成するために、ArcGIS Developer アカウント（無償）を作成し、API キーを取得する方法は[こちら](https://esrijapan.github.io/arcgis-dev-resources/guide/get-dev-account/)のガイドを参照してください。

