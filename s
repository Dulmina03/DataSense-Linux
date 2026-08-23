[1mdiff --git a/Views/DashboardView.axaml b/Views/DashboardView.axaml[m
[1mindex c89490c..3120ff9 100644[m
[1m--- a/Views/DashboardView.axaml[m
[1m+++ b/Views/DashboardView.axaml[m
[36m@@ -7,142 +7,128 @@[m
              x:Class="DataSense.Views.DashboardView">[m
              [m
     <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="{DynamicResource Padding.Large}">[m
[31m-        <StackPanel Spacing="{DynamicResource Spacing.XL}">[m
[32m+[m[32m        <StackPanel Spacing="{DynamicResource Spacing.XXL}">[m
             [m
[31m-            <!-- LEVEL 1: HERO & TODAY -->[m
[31m-            <Grid ColumnDefinitions="*,300">[m
[31m-                <!-- Live Network Hero -->[m
[31m-                <Border Grid.Column="0" Classes="CardElevated" Margin="0,0,16,0">[m
[31m-                    <Grid ColumnDefinitions="*,Auto">[m
[31m-                        <StackPanel Grid.Column="0" Spacing="{DynamicResource Spacing.SM}" VerticalAlignment="Center">[m
[31m-                            <TextBlock Text="Good afternoon" Classes="PageTitle" />[m
[31m-                            <TextBlock Text="Here's your network activity at a glance." Classes="BodySecondary" />[m
[31m-                            <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Spacing.SM}" Margin="0,16,0,0">[m
[31m-                                <Border Classes="Badge">[m
[31m-                                    <StackPanel Orientation="Horizontal" Spacing="6">[m
[31m-                                        <Ellipse Width="8" Height="8" Fill="{Binding StatusDotColor, Converter={x:Static conv:SemanticBrushConverter.Instance}}" VerticalAlignment="Center"/>[m
[31m-                                        <TextBlock Text="{Binding StatusText}" Foreground="{DynamicResource Brush.TextPrimary}" />[m
[31m-                                    </StackPanel>[m
[31m-                                </Border>[m
[31m-                                <Border Classes="Badge">[m
[31m-                                    <TextBlock Text="{Binding NetworkIdentityText}" Foreground="{DynamicResource Brush.TextSecondary}" />[m
[31m-                                </Border>[m
[31m-                            </StackPanel>[m
[31m-                        </StackPanel>[m
[31m-                        <StackPanel Grid.Column="1" Spacing="{DynamicResource Spacing.MD}" VerticalAlignment="Center">[m
[31m-                            <StackPanel Orientation="Horizontal" Spacing="8">[m
[31m-                                <TextBlock Text="⬇" Foreground="{DynamicResource Brush.Download}" Classes="MetricSmall" />[m
[31m-                                <TextBlock Text="{Binding DownloadSpeedText}" Foreground="{DynamicResource Brush.Download}" Classes="MetricLarge" />[m
[31m-                            </StackPanel>[m
[31m-                            <StackPanel Orientation="Horizontal" Spacing="8">[m
[31m-                                <TextBlock Text="⬆" Foreground="{DynamicResource Brush.Upload}" Classes="MetricSmall" />[m
[31m-                                <TextBlock Text="{Binding UploadSpeedText}" Foreground="{DynamicResource Brush.Upload}" Classes="MetricLarge" />[m
[31m-                            </StackPanel>[m
[31m-                        </StackPanel>[m
[31m-                    </Grid>[m
[31m-                </Border>[m
[32m+[m[32m            <!-- HEADER & LIVE HERO -->[m
[32m+[m[32m            <Grid ColumnDefinitions="*,Auto" Margin="0,0,0,8">[m
[32m+[m[32m                <StackPanel Spacing="{DynamicResource Spacing.XS}">[m
[32m+[m[32m                    <TextBlock Text="Overview" Classes="PageTitle" />[m
[32m+[m[32m                    <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Spacing.SM}">[m
[32m+[m[32m                        <Ellipse Width="8" Height="8" Fill="{Binding StatusDotColor, Converter={x:Static conv:SemanticBrushConverter.Instance}}" VerticalAlignment="Center"/>[m
[32m+[m[32m                        <TextBlock Text="{Binding StatusText}" Classes="BodySecondary" />[m
[32m+[m[32m                        <TextBlock Text="•" Classes="BodySecondary" />[m
[32m+[m[32m                        <TextBlock Text="{Binding ConnectionType}" Classes="BodySecondary" />[m
[32m+[m[32m                        <TextBlock Text="•" Classes="BodySecondary" IsVisible="{Binding HasWifi}" />[m
[32m+[m[32m                        <TextBlock Text="{Binding WifiSsid}" Classes="BodySecondary" Foreground="{DynamicResource Brush.Accent}" IsVisible="{Binding HasWifi}" />[m
[32m+[m[32m                    </StackPanel>[m
[32m+[m[32m                </StackPanel>[m
[32m+[m[32m            </Grid>[m
 [m
[31m-                <!-- Today's Primary Metrics -->[m
[31m-                <Border Grid.Column="1" Classes="Card">[m
[31m-                    <StackPanel Spacing="{DynamicResource Spacing.SM}">[m
[32m+[m[32m            <!-- HERO METRICS -->[m
[32m+[m[32m            <Grid ColumnDefinitions="*,*,*" Margin="0,0,0,0">[m
[32m+[m[32m                <Grid.Styles>[m
[32m+[m[32m                    <Style Selector="Border.HeroBox">[m
[32m+[m[32m                        <Setter Property="Padding" Value="0" />[m
[32m+[m[32m                        <Setter Property="Margin" Value="0,0,24,0" />[m
[32m+[m[32m                    </Style>[m
[32m+[m[32m                </Grid.Styles>[m
[32m+[m[41m                [m
[32m+[m[32m                <Border Grid.Column="0" Classes="HeroBox">[m
[32m+[m[32m                    <StackPanel Spacing="{DynamicResource Spacing.XS}">[m
[32m+[m[32m                        <TextBlock Text="Download" Classes="SectionTitle" />[m
[32m+[m[32m                        <TextBlock Text="{Binding DownloadSpeedText}" Classes="DisplayMetric" Foreground="{DynamicResource Brush.Download}" />[m
[32m+[m[32m                    </StackPanel>[m
[32m+[m[32m                </Border>[m
[32m+[m[32m                <Border Grid.Column="1" Classes="HeroBox">[m
[32m+[m[32m                    <StackPanel Spacing="{DynamicResource Spacing.XS}">[m
[32m+[m[32m                        <TextBlock Text="Upload" Classes="SectionTitle" />[m
[32m+[m[32m                        <TextBlock Text="{Binding UploadSpeedText}" Classes="DisplayMetric" Foreground="{DynamicResource Brush.Upload}" />[m
[32m+[m[32m                    </StackPanel>[m
[32m+[m[32m                </Border>[m
[32m+[m[32m                <Border Grid.Column="2" Classes="HeroBox" Margin="0">[m
[32m+[m[32m                    <StackPanel Spacing="{DynamicResource Spacing.XS}">[m
                         <TextBlock Text="Today's Usage" Classes="SectionTitle" />[m
[31m-                        <Grid ColumnDefinitions="*,*">[m
[31m-                            <StackPanel Grid.Column="0" Spacing="4">[m
[31m-                                <TextBlock Text="Downloaded" Classes="Caption" />[m
[31m-                                <TextBlock Text="{Binding TodayDownloadedText}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Download}" />[m
[31m-                            </StackPanel>[m
[31m-                            <StackPanel Grid.Column="1" Spacing="4">[m
[31m-                                <TextBlock Text="Uploaded" Classes="Caption" />[m
[31m-                                <TextBlock Text="{Binding TodayUploadedText}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Upload}" />[m
[31m-                            </StackPanel>[m
[31m-                        </Grid>[m
[31m-                        <StackPanel Spacing="4" Margin="0,8,0,0">[m
[31m-                            <TextBlock Text="Total" Classes="Caption" />[m
[31m-                            <TextBlock Text="{Binding TodayTotalText}" Classes="Metric" Foreground="{DynamicResource Brush.Accent}" />[m
[31m-                            <TextBlock Text="{Binding TodayVsYesterdayText}" Classes="Caption" IsVisible="{Binding HasTodayDelta}" />[m
[31m-                        </StackPanel>[m
[32m+[m[32m                        <TextBlock Text="{Binding TodayTotalText}" Classes="DisplayMetric" Foreground="{DynamicResource Brush.TextPrimary}" />[m
[32m+[m[32m                        <TextBlock Text="{Binding TodayVsYesterdayText}" Classes="Caption" IsVisible="{Binding HasTodayDelta}" Foreground="{Binding TodayDeltaColor, Converter={x:Static conv:SemanticBrushConverter.Instance}}" />[m
                     </StackPanel>[m
                 </Border>[m
             </Grid>[m
 [m
[31m-            <!-- LEVEL 2: TREND & CONSUMERS -->[m
[31m-            <Grid ColumnDefinitions="*,400">[m
[31m-                <!-- Usage Trend Chart -->[m
[31m-                <Border Grid.Column="0" Classes="Card" Margin="0,0,16,0">[m
[31m-                    <StackPanel Spacing="{DynamicResource Spacing.MD}">[m
[31m-                        <Grid ColumnDefinitions="*,Auto">[m
[31m-                            <TextBlock Grid.Column="0" Text="Usage Trend" Classes="SectionTitle" VerticalAlignment="Center"/>[m
[31m-                            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">[m
[31m-                                <Button Content="Today" Command="{Binding SelectPeriodCommand}" CommandParameter="Today" Classes="Icon"/>[m
[31m-                                <Button Content="7 Days" Command="{Binding SelectPeriodCommand}" CommandParameter="Last7Days" Classes="Icon"/>[m
[31m-                                <Button Content="30 Days" Command="{Binding SelectPeriodCommand}" CommandParameter="Last30Days" Classes="Icon"/>[m
[31m-                            </StackPanel>[m
[31m-                        </Grid>[m
[31m-                        [m
[31m-                        <!-- Empty / Loading States -->[m
[31m-                        <StackPanel Classes="EmptyState" IsVisible="{Binding IsChartEmpty}">[m
[31m-                            <TextBlock Text="📈" Classes="EmptyStateIcon" />[m
[31m-                            <TextBlock Text="No usage data available" Classes="EmptyStateTitle" />[m
[32m+[m[32m            <!-- PRIMARY CHART -->[m
[32m+[m[32m            <Border Classes="CardHero">[m
[32m+[m[32m                <StackPanel Spacing="{DynamicResource Spacing.LG}">[m
[32m+[m[32m                    <Grid ColumnDefinitions="*,Auto">[m
[32m+[m[32m                        <TextBlock Grid.Column="0" Text="Usage Trend" Classes="SectionTitle" VerticalAlignment="Center"/>[m
[32m+[m[32m                        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="{DynamicResource Spacing.XS}">[m
[32m+[m[32m                            <Button Content="Today" Command="{Binding SelectPeriodCommand}" CommandParameter="Today" Classes="Ghost"/>[m
[32m+[m[32m                            <Button Content="7 Days" Command="{Binding SelectPeriodCommand}" CommandParameter="Last7Days" Classes="Ghost"/>[m
[32m+[m[32m                            <Button Content="30 Days" Command="{Binding SelectPeriodCommand}" CommandParameter="Last30Days" Classes="Ghost"/>[m
                         </StackPanel>[m
[31m-                        <Border Classes="SkeletonBlock" Height="200" IsVisible="{Binding IsAnalyticsLoading}" />[m
[32m+[m[32m                    </Grid>[m
[32m+[m[41m                    [m
[32m+[m[32m                    <StackPanel Classes="EmptyState" IsVisible="{Binding IsChartEmpty}">[m
[32m+[m[32m                        <TextBlock Text="📈" Classes="EmptyStateIcon" />[m
[32m+[m[32m                        <TextBlock Text="No usage data available" Classes="EmptyStateTitle" />[m
[32m+[m[32m                    </StackPanel>[m
[32m+[m[32m                    <Border Classes="SkeletonBlock" Height="200" IsVisible="{Binding IsAnalyticsLoading}" />[m
 [m
[31m-                        <!-- Chart Canvas container matching previous implementation logic -->[m
[31m-                        <StackPanel IsVisible="{Binding !IsChartEmpty}" Spacing="0">[m
[31m-                            <Grid Height="160" SizeChanged="ChartContainer_SizeChanged">[m
[31m-                                <Canvas>[m
[31m-                                    <Line StartPoint="0,0"   EndPoint="2000,0"   Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.6"/>[m
[31m-                                    <Line StartPoint="0,40"  EndPoint="2000,40"  Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.4"/>[m
[31m-                                    <Line StartPoint="0,80"  EndPoint="2000,80"  Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.4"/>[m
[31m-                                    <Line StartPoint="0,120" EndPoint="2000,120" Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.4"/>[m
[31m-                                    <Line StartPoint="0,160" EndPoint="2000,160" Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.6"/>[m
[31m-                                </Canvas>[m
[31m-                                <ItemsControl ItemsSource="{Binding DailyChartItems}">[m
[31m-                                    <ItemsControl.ItemsPanel>[m
[31m-                                        <ItemsPanelTemplate><Canvas Height="160"/></ItemsPanelTemplate>[m
[31m-                                    </ItemsControl.ItemsPanel>[m
[31m-                                    <ItemsControl.ItemTemplate>[m
[31m-                                        <DataTemplate DataType="vm:DailyChartBarViewModel">[m
[31m-                                            <Canvas>[m
[31m-                                                <Rectangle Canvas.Left="{Binding BarX}" Canvas.Top="{Binding UploadBarY}" Width="{Binding BarWidth}" Height="{Binding UploadBarHeight}" Fill="{DynamicResource Brush.Upload}" Opacity="0.85" RadiusX="2" RadiusY="2" ToolTip.Tip="{Binding Tooltip}"/>[m
[31m-                                                <Rectangle Canvas.Left="{Binding BarX}" Canvas.Top="{Binding DownloadBarY}" Width="{Binding BarWidth}" Height="{Binding DownloadBarHeight}" Fill="{DynamicResource Brush.Download}" Opacity="0.85" ToolTip.Tip="{Binding Tooltip}"/>[m
[31m-                                            </Canvas>[m
[31m-                                        </DataTemplate>[m
[31m-                                    </ItemsControl.ItemTemplate>[m
[31m-                                </ItemsControl>[m
[31m-                            </Grid>[m
[32m+[m[32m                    <StackPanel IsVisible="{Binding !IsChartEmpty}" Spacing="0">[m
[32m+[m[32m                        <Grid Height="160" SizeChanged="ChartContainer_SizeChanged">[m
[32m+[m[32m                            <Canvas>[m
[32m+[m[32m                                <Line StartPoint="0,0"   EndPoint="4000,0"   Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.5"/>[m
[32m+[m[32m                                <Line StartPoint="0,40"  EndPoint="4000,40"  Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.3"/>[m
[32m+[m[32m                                <Line StartPoint="0,80"  EndPoint="4000,80"  Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.3"/>[m
[32m+[m[32m                                <Line StartPoint="0,120" EndPoint="4000,120" Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.3"/>[m
[32m+[m[32m                                <Line StartPoint="0,160" EndPoint="4000,160" Stroke="{DynamicResource Brush.Border}" StrokeThickness="1" Opacity="0.5"/>[m
[32m+[m[32m                            </Canvas>[m
                             <ItemsControl ItemsSource="{Binding DailyChartItems}">[m
[31m-                                <ItemsControl.ItemsPanel><ItemsPanelTemplate><Canvas Height="28"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>[m
[32m+[m[32m                                <ItemsControl.ItemsPanel>[m
[32m+[m[32m                                    <ItemsPanelTemplate><Canvas Height="160"/></ItemsPanelTemplate>[m
[32m+[m[32m                                </ItemsControl.ItemsPanel>[m
                                 <ItemsControl.ItemTemplate>[m
                                     <DataTemplate DataType="vm:DailyChartBarViewModel">[m
[31m-                                        <TextBlock Canvas.Left="{Binding BarX}" Canvas.Top="4" Text="{Binding DayLabel}" Classes="Caption" Width="{Binding BarWidth}" TextAlignment="Center"/>[m
[32m+[m[32m                                        <Canvas>[m
[32m+[m[32m                                            <Rectangle Canvas.Left="{Binding BarX}" Canvas.Top="{Binding UploadBarY}" Width="{Binding BarWidth}" Height="{Binding UploadBarHeight}" Fill="{DynamicResource Brush.Upload}" Opacity="0.9" RadiusX="2" RadiusY="2" ToolTip.Tip="{Binding Tooltip}"/>[m
[32m+[m[32m                                            <Rectangle Canvas.Left="{Binding BarX}" Canvas.Top="{Binding DownloadBarY}" Width="{Binding BarWidth}" Height="{Binding DownloadBarHeight}" Fill="{DynamicResource Brush.Download}" Opacity="0.9" ToolTip.Tip="{Binding Tooltip}"/>[m
[32m+[m[32m                                        </Canvas>[m
                                     </DataTemplate>[m
                                 </ItemsControl.ItemTemplate>[m
                             </ItemsControl>[m
[31m-                        </StackPanel>[m
[32m+[m[32m                        </Grid>[m
[32m+[m[32m                        <ItemsControl ItemsSource="{Binding DailyChartItems}" Margin="0,12,0,0">[m
[32m+[m[32m                            <ItemsControl.ItemsPanel><ItemsPanelTemplate><Canvas Height="28"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>[m
[32m+[m[32m                            <ItemsControl.ItemTemplate>[m
[32m+[m[32m                                <DataTemplate DataType="vm:DailyChartBarViewModel">[m
[32m+[m[32m                                    <TextBlock Canvas.Left="{Binding BarX}" Canvas.Top="0" Text="{Binding DayLabel}" Classes="Caption" Width="{Binding BarWidth}" TextAlignment="Center"/>[m
[32m+[m[32m                                </DataTemplate>[m
[32m+[m[32m                            </ItemsControl.ItemTemplate>[m
[32m+[m[32m                        </ItemsControl>[m
                     </StackPanel>[m
[31m-                </Border>[m
[31m-                [m
[32m+[m[32m                </StackPanel>[m
[32m+[m[32m            </Border>[m
[32m+[m
[32m+[m[32m            <!-- SECONDARY ANALYTICS (Consumers & Distribution) -->[m
[32m+[m[32m            <Grid ColumnDefinitions="*,*">[m
                 <!-- Top Consumers -->[m
[31m-                <Border Grid.Column="1" Classes="Card">[m
[32m+[m[32m                <Border Grid.Column="0" Classes="CardInteractive" Margin="0,0,16,0">[m
                     <StackPanel Spacing="{DynamicResource Spacing.MD}">[m
                         <TextBlock Text="Top Data Consumers" Classes="SectionTitle" />[m
                         [m
[31m-                        <StackPanel Classes="EmptyState" IsVisible="{Binding !HasTopProcesses}">[m
[32m+[m[32m                        <StackPanel Classes="EmptyState" IsVisible="{Binding !HasTopProcesses}" Margin="0,32">[m
                             <TextBlock Text="📱" Classes="EmptyStateIcon" />[m
                             <TextBlock Text="No application traffic available" Classes="EmptyStateTitle" />[m
                         </StackPanel>[m
                         [m
[31m-                        <!-- List of consumers -->[m
[31m-                        <ItemsControl ItemsSource="{Binding TopProcesses}">[m
[32m+[m[32m                        <ItemsControl ItemsSource="{Binding TopProcesses}" IsVisible="{Binding HasTopProcesses}">[m
                             <ItemsControl.ItemTemplate>[m
                                 <DataTemplate>[m
[31m-                                    <Border BorderBrush="{DynamicResource Brush.Border}" BorderThickness="0,0,0,1" Padding="8">[m
[32m+[m[32m                                    <Border BorderBrush="{DynamicResource Brush.Border}" BorderThickness="0,0,0,1" Padding="0,12">[m
                                         <Grid ColumnDefinitions="*,Auto">[m
[31m-                                            <StackPanel Grid.Column="0">[m
[32m+[m[32m                                            <StackPanel Grid.Column="0" VerticalAlignment="Center">[m
                                                 <TextBlock Text="{Binding ProcessName}" Classes="Body" FontWeight="SemiBold" />[m
                                                 <TextBlock Text="{Binding DataSource}" Classes="Caption" />[m
                                             </StackPanel>[m
[31m-                                            <TextBlock Grid.Column="1" Text="{Binding TodayBytes, Converter={x:Static conv:ByteFormatConverter.Instance}}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Accent}" VerticalAlignment="Center" />[m
[32m+[m[32m                                            <TextBlock Grid.Column="1" Text="{Binding TodayBytes, Converter={x:Static conv:ByteFormatConverter.Instance}}" Classes="MetricSmall" Foreground="{DynamicResource Brush.TextPrimary}" VerticalAlignment="Center" />[m
                                         </Grid>[m
                                     </Border>[m
                                 </DataTemplate>[m
[36m@@ -150,168 +136,156 @@[m
                         </ItemsControl>[m
                     </StackPanel>[m
                 </Border>[m
[31m-            </Grid>[m
[31m-[m
[31m-            <!-- LEVEL 3: SESSION, CONNECTION, RATIO -->[m
[31m-            <Grid ColumnDefinitions="*,*,*">[m
[31m-                <!-- Session -->[m
[31m-                <Border Grid.Column="0" Classes="CardInteractive" Margin="0,0,16,0">[m
[31m-                    <StackPanel Spacing="{DynamicResource Spacing.SM}">[m
[31m-                        <Grid ColumnDefinitions="*,Auto">[m
[31m-                            <TextBlock Grid.Column="0" Text="Current Session" Classes="SectionTitle" />[m
[31m-                            <Button Grid.Column="1" Content="➔" Classes="Icon" Command="{Binding OpenTimelineCommand}"/>[m
[31m-                        </Grid>[m
[31m-                        [m
[31m-                        <StackPanel Classes="EmptyState" IsVisible="{Binding !HasCurrentSession}">[m
[31m-                            <TextBlock Text="No active network session" Classes="EmptyStateTitle" />[m
[31m-                        </StackPanel>[m
[31m-                        [m
[31m-                        <StackPanel IsVisible="{Binding HasCurrentSession}" Spacing="8">[m
[31m-                            <TextBlock Text="{Binding CurrentSessionNetwork}" Classes="Body" FontWeight="SemiBold" />[m
[31m-                            <TextBlock Text="{Binding CurrentSessionDuration}" Classes="Caption" />[m
[31m-                            <Grid ColumnDefinitions="*,*">[m
[31m-                                <StackPanel>[m
[31m-                                    <TextBlock Text="Downloaded" Classes="Caption"/>[m
[31m-                                    <TextBlock Text="{Binding CurrentSessionDownload}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Download}"/>[m
[31m-                                </StackPanel>[m
[31m-                                <StackPanel Grid.Column="1">[m
[31m-                                    <TextBlock Text="Uploaded" Classes="Caption"/>[m
[31m-                                    <TextBlock Text="{Binding CurrentSessionUpload}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Upload}"/>[m
[31m-                                </StackPanel>[m
[31m-                            </Grid>[m
[31m-                        </StackPanel>[m
[31m-                    </StackPanel>[m
[31m-                </Border>[m
                 [m
[31m-                <!-- Connection -->[m
[31m-                <Border Grid.Column="1" Classes="Card" Margin="0,0,16,0">[m
[31m-                    <StackPanel Spacing="{DynamicResource Spacing.SM}">[m
[31m-                        <TextBlock Text="Connection" Classes="SectionTitle" />[m
[31m-                        <Grid ColumnDefinitions="100,*">[m
[31m-                            <TextBlock Grid.Column="0" Text="Type:" Classes="Caption" />[m
[31m-                            <TextBlock Grid.Column="1" Text="{Binding ConnectionType}" Classes="Body" />[m
[32m+[m[32m                <!-- Distribution & Session -->[m
[32m+[m[32m                <StackPanel Grid.Column="1" Spacing="{DynamicResource Spacing.XL}">[m
[32m+[m[41m                    [m
[32m+[m[32m                    <!-- Month Ratio -->[m
[32m+[m[32m                    <Border Classes="Card">[m
[32m+[m[32m                        <StackPanel Spacing="{DynamicResource Spacing.MD}">[m
[32m+[m[32m                            <TextBlock Text="Month Distribution" Classes="SectionTitle" />[m
[32m+[m[32m                            <TextBlock Text="No data recorded this month." Classes="BodySecondary" IsVisible="{Binding !HasMonthData}" />[m
                             [m
[31m-                            <TextBlock Grid.Row="1" Grid.Column="0" Text="State:" Classes="Caption" />[m
[31m-                            <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding ConnectionState}" Classes="Body" />[m
[32m+[m[32m                            <StackPanel IsVisible="{Binding HasMonthData}" Spacing="16">[m
[32m+[m[32m                                <Border CornerRadius="{DynamicResource Radius.Pill}" ClipToBounds="True" Height="8" Background="{DynamicResource Brush.SurfaceElevated}">[m
[32m+[m[32m                                    <Grid>[m
[32m+[m[32m                                        <Grid.ColumnDefinitions>[m
[32m+[m[32m                                            <ColumnDefinition Width="{Binding DownloadColumnWidth}"/>[m
[32m+[m[32m                                            <ColumnDefinition Width="{Binding UploadColumnWidth}"/>[m
[32m+[m[32m                                        </Grid.ColumnDefinitions>[m
[32m+[m[32m                                        <Rectangle Grid.Column="0" Fill="{DynamicResource Brush.Download}" />[m
[32m+[m[32m                                        <Rectangle Grid.Column="1" Fill="{DynamicResource Brush.Upload}" />[m
[32m+[m[32m                                    </Grid>[m
[32m+[m[32m                                </Border>[m
[32m+[m[32m                                <Grid ColumnDefinitions="*,*">[m
[32m+[m[32m                                    <StackPanel Grid.Column="0">[m
[32m+[m[32m                                        <TextBlock Text="{Binding DownloadActualText}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Download}"/>[m
[32m+[m[32m                                        <TextBlock Text="Downloaded" Classes="Caption" />[m
[32m+[m[32m                                    </StackPanel>[m
[32m+[m[32m                                    <StackPanel Grid.Column="1" HorizontalAlignment="Right">[m
[32m+[m[32m                                        <TextBlock Text="{Binding UploadActualText}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Upload}" TextAlignment="Right"/>[m
[32m+[m[32m                                        <TextBlock Text="Uploaded" Classes="Caption" TextAlignment="Right"/>[m
[32m+[m[32m                                    </StackPanel>[m
[32m+[m[32m                                </Grid>[m
[32m+[m[32m                            </StackPanel>[m
[32m+[m[32m                        </StackPanel>[m
[32m+[m[32m                    </Border>[m
[32m+[m[41m                    [m
[32m+[m[32m                    <!-- Session -->[m
[32m+[m[32m                    <Border Classes="CardInteractive">[m
[32m+[m[32m                        <StackPanel Spacing="{DynamicResource Spacing.MD}">[m
[32m+[m[32m                            <Grid ColumnDefinitions="*,Auto">[m
[32m+[m[32m                                <TextBlock Grid.Column="0" Text="Current Session" Classes="SectionTitle" />[m
[32m+[m[32m                                <Button Grid.Column="1" Content="➔" Classes="Icon" Command="{Binding OpenTimelineCommand}"/>[m
[32m+[m[32m                            </Grid>[m
                             [m
[31m-                            <TextBlock Grid.Row="2" Grid.Column="0" Text="SSID:" Classes="Caption" IsVisible="{Binding HasWifi}" />[m
[31m-                            <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding WifiSsid}" Classes="Body" IsVisible="{Binding HasWifi}" Foreground="{DynamicResource Brush.Accent}" />[m
[32m+[m[32m                            <StackPanel IsVisible="{Binding !HasCurrentSession}" Margin="0,16">[m
[32m+[m[32m                                <TextBlock Text="No active network session" Classes="BodySecondary" />[m
[32m+[m[32m                            </StackPanel>[m
                             [m
[31m-                            <TextBlock Grid.Row="3" Grid.Column="0" Text="Link Speed:" Classes="Caption" />[m
[31m-                            <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding LinkSpeed}" Classes="Body" />[m
[31m-                        </Grid>[m
[31m-                    </StackPanel>[m
[31m-                </Border>[m
[31m-                [m
[31m-                <!-- Download/Upload Ratio -->[m
[31m-                <Border Grid.Column="2" Classes="Card">[m
[31m-                    <StackPanel Spacing="{DynamicResource Spacing.SM}">[m
[31m-                        <TextBlock Text="Download vs Upload" Classes="SectionTitle" />[m
[31m-                        <TextBlock Text="No data recorded this month." Classes="EmptyStateTitle" IsVisible="{Binding !HasMonthData}" />[m
[31m-                        [m
[31m-                        <StackPanel IsVisible="{Binding HasMonthData}" Spacing="16" Margin="0,12,0,0">[m
[31m-                            <!-- Ratio Bar -->[m
[31m-                            <Border CornerRadius="{DynamicResource Radius.Small}" ClipToBounds="True" Height="16" Background="{DynamicResource Brush.SurfaceElevated}">[m
[31m-                                <Grid>[m
[31m-                                    <Grid.ColumnDefinitions>[m
[31m-                                        <ColumnDefinition Width="{Binding DownloadColumnWidth}"/>[m
[31m-                                        <ColumnDefinition Width="{Binding UploadColumnWidth}"/>[m
[31m-                                    </Grid.ColumnDefinitions>[m
[31m-                                    <Rectangle Grid.Column="0" Fill="{DynamicResource Brush.Download}" Opacity="0.85"/>[m
[31m-                                    <Rectangle Grid.Column="1" Fill="{DynamicResource Brush.Upload}" Opacity="0.85"/>[m
[32m+[m[32m                            <StackPanel IsVisible="{Binding HasCurrentSession}" Spacing="12">[m
[32m+[m[32m                                <Grid ColumnDefinitions="Auto,*">[m
[32m+[m[32m                                    <TextBlock Grid.Column="0" Text="Network:" Classes="BodySecondary" Width="80"/>[m
[32m+[m[32m                                    <TextBlock Grid.Column="1" Text="{Binding CurrentSessionNetwork}" Classes="Body" FontWeight="SemiBold" />[m
                                 </Grid>[m
[31m-                            </Border>[m
[31m-                            <Grid ColumnDefinitions="*,*">[m
[31m-                                <StackPanel Grid.Column="0">[m
[31m-                                    <TextBlock Text="{Binding DownloadActualText}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Download}"/>[m
[31m-                                    <TextBlock Text="{Binding DownloadRatioText}" Classes="Caption" />[m
[31m-                                </StackPanel>[m
[31m-                                <StackPanel Grid.Column="1">[m
[31m-                                    <TextBlock Text="{Binding UploadActualText}" Classes="MetricSmall" Foreground="{DynamicResource Brush.Upload}"/>[m
[31m-                                    <TextBlock Text="{Binding UploadRatioText}" Classes="Caption" />[m
[31m-                                </StackPanel>[m
[31m-                            </Grid>[m
[32m+[m[32m                                <Grid ColumnDefinitions="Auto,*">[m
[32m+[m[32m                                    <TextBlock Grid.Column="0" Text="Duration:" Classes="BodySecondary" Width="80"/>[m
[32m+[m[32m                                    <TextBlock Grid.Column="1" Text="{Binding CurrentSessionDuration}" Classes="Body" />[m
[32m+[m[32m                                </Grid>[m
[32m+[m[32m                                <Grid ColumnDefinitions="Auto,*">[m
[32m+[m[32m                                    <TextBlock Grid.Column="0" Text="Traffic:" Classes="BodySecondary" Width="80"/>[m
[32m+[m[32m                                    <TextBlock Grid.Column="1" Text="{Binding CurrentSessionTotal}" Classes="Body" />[m
[32m+[m[32m                                </Grid>[m
[32m+[m[32m                            </StackPanel>[m
                         </StackPanel>[m
[31m-                    </StackPanel>[m
[31m-                </Border>[m
[32m+[m[32m                    </Border>[m
[32m+[m[32m                </StackPanel>[m
             </Grid>[m
 [m
[31m-            <!-- LEVEL 4: FORECAST, BUDGET, INSIGHTS -->[m
[31m-            <Grid ColumnDefinitions="*,*,*">[m
[31m-                <!-- Forecast -->[m
[31m-                <Border Grid.Column="0" Classes="Card" Margin="0,0,16,0">[m
[31m-                    <StackPanel Spacing="{DynamicResource Spacing.SM}">[m
[31m-                        <TextBlock Text="Forecast" Classes="SectionTitle" />[m
[31m-                        <StackPanel Classes="EmptyState" IsVisible="{Binding !HasForecast}">[m
[31m-                            <TextBlock Text="Building your forecast baseline" Classes="EmptyStateTitle" />[m
[31m-                        </StackPanel>[m
[31m-                        [m
[31m-                        <StackPanel IsVisible="{Binding HasForecast}" Spacing="8">[m
[31m-                            <TextBlock Text="Projected Month-End" Classes="Caption" />[m
[31m-                            <TextBlock Text="{Binding ForecastProjectedText}" Classes="MetricLarge" Foreground="{DynamicResource Brush.Accent}" />[m
[31m-                            <Grid ColumnDefinitions="*,*">[m
[31m-                                <StackPanel>[m
[31m-                                    <TextBlock Text="Avg Daily" Classes="Caption" />[m
[31m-                                    <TextBlock Text="{Binding ForecastAvgDailyText}" Classes="Body" FontWeight="SemiBold" />[m
[31m-                                </StackPanel>[m
[31m-                                <StackPanel Grid.Column="1">[m
[31m-                                    <TextBlock Text="Confidence" Classes="Caption" />[m
[31m-                                    <TextBlock Text="{Binding ForecastConfidenceText}" Classes="Body" FontWeight="SemiBold" />[m
[31m-                                </StackPanel>[m
[31m-                            </Grid>[m
[31m-                        </StackPanel>[m
[31m-                    </StackPanel>[m
[31m-                </Border>[m
[31m-[m
[31m-                <!-- Budget -->[m
[31m-                <Border Grid.Column="1" Classes="Card" Margin="0,0,16,0">[m
[31m-                    <StackPanel Spacing="{DynamicResource Spacing.SM}">[m
[31m-                        <TextBlock Text="Data Budget" Classes="SectionTitle" />[m
[31m-                        <StackPanel Classes="EmptyState" IsVisible="{Binding !HasBudget}">[m
[31m-                            <TextBlock Text="No active data budget" Classes="EmptyStateTitle" />[m
[31m-                        </StackPanel>[m
[31m-                        [m
[31m-                        <StackPanel IsVisible="{Binding HasBudget}" Spacing="8">[m
[31m-                            <TextBlock Text="{Binding BudgetStatusText}" Classes="Body" FontWeight="Bold" Foreground="{DynamicResource Brush.Warning}" />[m
[31m-                            <ProgressBar Value="{Binding BudgetProgressValue}" Minimum="0" Maximum="100" Height="8" CornerRadius="4" BorderThickness="0" Foreground="{DynamicResource Brush.Warning}" Background="{DynamicResource Brush.SurfaceElevated}" />[m
[31m-                            <Grid ColumnDefinitions="*,*">[m
[31m-                                <StackPanel>[m
[31m-                                    <TextBlock Text="Used" Classes="Caption" />[m
[31m-                                    <TextBlock Text="{Binding BudgetUsedText}" Classes="Body" FontWeight="SemiBold" />[m
[31m-                                </StackPanel>[m
[31m-                                <StackPanel Grid.Column="1">[m
[31m-                                    <TextBlock Text="Remaining" Classes="Caption" />[m
[31m-                                    <TextBlock Text="{Binding BudgetRemainingText}" Classes="Body" FontWeight="SemiBold" />[m
[31m-                                </StackPanel>[m
[31m-                            </Grid>[m
[31m-                        </StackPanel>[m
[31m-                    </StackPanel>[m
[31m-                </Border>[m
[32m+[m[32m            <!-- TERTIARY: INTELLIGENCE & FORECAST -->[m
[32m+[m[32m            <Grid ColumnDefinitions="*,*">[m
                 [m
[31m-                <!-- Insights -->[m
[31m-                <Border Grid.Column="2" Classes="CardInteractive">[m
[31m-                    <StackPanel Spacing="{DynamicResource Spacing.SM}">[m
[32m+[m[32m                <!-- Insights / Health -->[m
[32m+[m[32m                <Border Grid.Column="0" Classes="CardInteractive" Margin="0,0,16,0">[m
[32m+[m[32m                    <StackPanel Spacing="{DynamicResource Spacing.MD}">[m
                         <Grid ColumnDefinitions="*,Auto">[m
[31m-                            <TextBlock Grid.Column="0" Text="Network Insights" Classes="SectionTitle" />[m
[32m+[m[32m                            <TextBlock Grid.Column="0" Text="Network Health" Classes="SectionTitle" />[m
                             <Button Grid.Column="1" Content="➔" Classes="Icon" Command="{Binding NavigateToUnifiedIntelligenceCommand}"/>[m
                         </Grid>[m
                         [m
[31m-                        <StackPanel Classes="EmptyState" IsVisible="{Binding !HasInsights}">[m
[32m+[m[32m                        <StackPanel Classes="EmptyState" IsVisible="{Binding !HasInsights}" Margin="0,32">[m
                             <TextBlock Text="Everything looks normal" Classes="EmptyStateTitle" />[m
                         </StackPanel>[m
                         [m
                         <ItemsControl ItemsSource="{Binding Insights}" IsVisible="{Binding HasInsights}">[m
                             <ItemsControl.ItemTemplate>[m
                                 <DataTemplate>[m
[31m-                                    <StackPanel Margin="0,4">[m
[31m-                                        <TextBlock Text="{Binding Title}" Classes="Body" FontWeight="SemiBold" />[m
[31m-                                        <TextBlock Text="{Binding Description}" Classes="Caption" TextWrapping="Wrap" />[m
[31m-                                    </StackPanel>[m
[32m+[m[32m                                    <Border BorderBrush="{DynamicResource Brush.Border}" BorderThickness="0,0,0,1" Padding="0,12">[m
[32m+[m[32m                                        <StackPanel>[m
[32m+[m[32m                                            <TextBlock Text="{Binding Title}" Classes="Body" FontWeight="SemiBold" />[m
[32m+[m[32m                                            <TextBlock Text="{Binding Description}" Classes="Caption" TextWrapping="Wrap" Margin="0,4,0,0" />[m
[32m+[m[32m                                        </StackPanel>[m
[32m+[m[32m                                    </Border>[m
                                 </DataTemplate>[m
                             </ItemsControl.ItemTemplate>[m
                         </ItemsControl>[m
                     </StackPanel>[m
                 </Border>[m
[32m+[m[41m                [m
[32m+[m[32m                <StackPanel Grid.Column="1" Spacing="{DynamicResource Spacing.XL}">[m
[32m+[m[32m                    <!-- Budget -->[m
[32m+[m[32m                    <Border Classes="Card">[m
[32m+[m[32m                        <StackPanel Spacing="{DynamicResource Spacing.MD}">[m
[32m+[m[32m                            <TextBlock Text="Data Budget" Classes="SectionTitle" />[m
[32m+[m[32m                            <StackPanel IsVisible="{Binding !HasBudget}" Margin="0,16">[m
[32m+[m[32m                                <TextBlock Text="No active data budget" Classes="BodySecondary" />[m
[32m+[m[32m                            </StackPanel>[m
[32m+[m[41m                            [m
[32m+[m[32m                            <StackPanel IsVisible="{Binding HasBudget}" Spacing="12">[m
[32m+[m[32m                                <TextBlock Text="{Binding BudgetStatusText}" Classes="Body" FontWeight="Bold" Foreground="{DynamicResource Brush.Warning}" />[m
[32m+[m[32m                                <ProgressBar Value="{Binding BudgetProgressValue}" Minimum="0" Maximum="100" Height="8" CornerRadius="4" BorderThickness="0" Foreground="{DynamicResource Brush.Warning}" Background="{DynamicResource Brush.SurfaceElevated}" />[m
[32m+[m[32m                                <Grid ColumnDefinitions="*,*">[m
[32m+[m[32m                                    <StackPanel>[m
[32m+[m[32m                                        <TextBlock Text="{Binding BudgetUsedText}" Classes="MetricSmall" />[m
[32m+[m[32m                                        <TextBlock Text="Used" Classes="Caption" />[m
[32m+[m[32m                                    </StackPanel>[m
[32m+[m[32m                                    <StackPanel Grid.Column="1" HorizontalAlignment="Right">[m
[32m+[m[32m                                        <TextBlock Text="{Binding BudgetRemainingText}" Classes="MetricSmall" TextAlignment="Right" />[m
[32m+[m[32m                                        <TextBlock Text="Remaining" Classes="Caption" TextAlignment="Right"/>[m
[32m+[m[32m                                    </StackPanel>[m
[32m+[m[32m                                </Grid>[m
[32m+[m[32m                            </StackPanel>[m
[32m+[m[32m                        </StackPanel>[m
[32m+[m[32m                    </Border>[m
[32m+[m[41m                    [m
[32m+[m[32m                    <!-- Forecast -->[m
[32m+[m[32m                    <Border Classes="Card">[m
[32m+[m[32m                        <StackPanel Spacing="{DynamicResource Spacing.MD}">[m
[32m+[m[32m                            <TextBlock Text="Forecast" Classes="SectionTitle" />[m
[32m+[m[32m                            <StackPanel IsVisible="{Binding !HasForecast}" Margin="0,16">[m
[32m+[m[32m                                <TextBlock Text="Building your forecast baseline" Classes="BodySecondary" />[m
[32m+[m[32m                            </StackPanel>[m
[32m+[m[41m                            [m
[32m+[m[32m                            <StackPanel IsVisible="{Binding HasForecast}" Spacing="12">[m
[32m+[m[32m                                <StackPanel>[m
[32m+[m[32m                                    <TextBlock Text="{Binding ForecastProjectedText}" Classes="LargeMetric" Foreground="{DynamicResource Brush.Accent}" />[m
[32m+[m[32m                                    <TextBlock Text="Projected Month-End" Classes="Caption" />[m
[32m+[m[32m                                </StackPanel>[m
[32m+[m[32m                                <Grid ColumnDefinitions="*,*">[m
[32m+[m[32m                                    <StackPanel>[m
[32m+[m[32m                                        <TextBlock Text="{Binding ForecastAvgDailyText}" Classes="Body" FontWeight="SemiBold" />[m
[32m+[m[32m                                        <TextBlock Text="Avg Daily" Classes="Caption" />[m
[32m+[m[32m                                    </StackPanel>[m
[32m+[m[32m                                    <StackPanel Grid.Column="1" HorizontalAlignment="Right">[m
[32m+[m[32m                                        <TextBlock Text="{Binding ForecastConfidenceText}" Classes="Body" FontWeight="SemiBold" TextAlignment="Right"/>[m
[32m+[m[32m                                        <TextBlock Text="Confidence" Classes="Caption" TextAlignment="Right"/>[m
[32m+[m[32m                                    </StackPanel>[m
[32m+[m[32m                                </Grid>[m
[32m+[m[32m                            </StackPanel>[m
[32m+[m[32m                        </StackPanel>[m
[32m+[m[32m                    </Border>[m
[32m+[m[32m                </StackPanel>[m
[32m+[m
             </Grid>[m
             [m
         </StackPanel>[m
