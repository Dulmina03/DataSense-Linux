import re
import sys

file_path = "/home/dulmina/Projects/DataSense/Views/HistoryView.axaml"

with open(file_path, "r") as f:
    content = f.read()

# Styles
content = re.sub(r'<UserControl\.Styles>.*?</UserControl\.Styles>\n', '', content, flags=re.DOTALL)
content = content.replace('Classes="summary-card"', 'Classes="Card"')
content = content.replace('Classes="day-row"', 'Classes="Card"')

# Backgrounds & Borders
content = content.replace('Background="#0D0D18"', 'Background="{DynamicResource Brush.AppBackground}"')
content = content.replace('Background="#13131F"', 'Background="{DynamicResource Brush.Surface}"')
content = content.replace('BorderBrush="#1E1E30"', 'BorderBrush="{DynamicResource Brush.Border}"')
content = content.replace('Background="#1A1A2E"', 'Background="{DynamicResource Brush.SurfaceElevated}"')
content = content.replace('BorderBrush="#2D2D44"', 'BorderBrush="{DynamicResource Brush.BorderStrong}"')
content = content.replace('Background="#0F0F1C"', 'Background="{DynamicResource Brush.Surface}"')
content = content.replace('Background="#10101C"', 'Background="{DynamicResource Brush.AppBackground}"')
content = content.replace('Background="#1A1A3A"', 'Background="{DynamicResource Brush.SurfaceElevated}"')
content = content.replace('BorderBrush="#3535AA"', 'BorderBrush="{DynamicResource Brush.Accent}"')
content = content.replace('Background="#1A0A0A"', 'Background="{DynamicResource Brush.DangerSurface}"')

# Texts
content = content.replace('Foreground="#CCCCDD"', 'Foreground="{DynamicResource Brush.TextPrimary}"')
content = content.replace('Foreground="#CCCCEE"', 'Foreground="{DynamicResource Brush.TextPrimary}"')
content = content.replace('Foreground="#666688"', 'Foreground="{DynamicResource Brush.TextSecondary}"')
content = content.replace('Foreground="#8888FF"', 'Foreground="{DynamicResource Brush.Accent}"')
content = content.replace('Foreground="#888899"', 'Foreground="{DynamicResource Brush.TextSecondary}"')
content = content.replace('Foreground="#7777BB"', 'Foreground="{DynamicResource Brush.TextSecondary}"')
content = content.replace('Foreground="#44445A"', 'Foreground="{DynamicResource Brush.TextMuted}"')
content = content.replace('Foreground="#336688"', 'Foreground="{DynamicResource Brush.TextMuted}"')
content = content.replace('Foreground="#226633"', 'Foreground="{DynamicResource Brush.TextMuted}"')
content = content.replace('Foreground="#00D2FF"', 'Foreground="{DynamicResource Brush.Download}"')
content = content.replace('Foreground="#00E676"', 'Foreground="{DynamicResource Brush.Upload}"')
content = content.replace('Foreground="#446644"', 'Foreground="{DynamicResource Brush.TextMuted}"')
content = content.replace('Foreground="#88BBAA"', 'Foreground="{DynamicResource Brush.Success}"')
content = content.replace('Foreground="#FF6666"', 'Foreground="{DynamicResource Brush.Danger}"')
content = content.replace('Foreground="#CC5555"', 'Foreground="{DynamicResource Brush.Danger}"')
content = content.replace('Foreground="#555577"', 'Foreground="{DynamicResource Brush.TextMuted}"')
content = content.replace('Foreground="#555599"', 'Foreground="{DynamicResource Brush.TextMuted}"')
content = content.replace('Foreground="#55556A"', 'Foreground="{DynamicResource Brush.TextMuted}"')
content = content.replace('Foreground="#AAAACC"', 'Foreground="{DynamicResource Brush.TextSecondary}"')

# Remaining colors cleanup
content = re.sub(r'Foreground="#[A-Fa-f0-9]{6}"', 'Foreground="{DynamicResource Brush.TextMuted}"', content)
content = re.sub(r'Background="#[A-Fa-f0-9]{6}"', 'Background="{DynamicResource Brush.Surface}"', content)
content = re.sub(r'BorderBrush="#[A-Fa-f0-9]{6}"', 'BorderBrush="{DynamicResource Brush.Border}"', content)

with open(file_path, "w") as f:
    f.write(content)
