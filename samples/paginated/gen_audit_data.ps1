$out = "audit_logs.csv"
"timestamp,user_id,event_type,resource,status,duration_ms" | Out-File $out -Encoding utf8

$users = @("admin", "jdoe", "asmith", "bwhite", "clane", "davis")
$events = @("LOGIN", "LOGOUT", "VIEW_REPORT", "EXPORT_REPORT", "CREATE_USER", "DELETE_USER", "ADMIN_CONFIG")
$resources = @("SalesDashboard", "UserManagement", "SystemSettings", "InventoryReport", "AuditLogs")

$startDate = (Get-Date).AddMonths(-6)
$endDate = Get-Date

for ($date = $startDate; $date -le $endDate; $date = $date.AddDays(1)) {
    $isWeekend = ($date.DayOfWeek -eq "Saturday" -or $date.DayOfWeek -eq "Sunday")
    
    # Number of events per day
    $numEvents = if ($isWeekend) { Get-Random -Minimum 5 -Maximum 15 } else { Get-Random -Minimum 50 -Maximum 150 }
    
    for ($i = 0; $i -lt $numEvents; $i++) {
        $hour = if ($isWeekend) { 
            Get-Random -Minimum 0 -Maximum 24 
        } else {
            # 80% chance for 8-5
            if ((Get-Random -Minimum 0 -Maximum 100) -lt 80) {
                Get-Random -Minimum 8 -Maximum 17
            } else {
                Get-Random -Minimum 0 -Maximum 24
            }
        }
        
        $minute = Get-Random -Minimum 0 -Maximum 60
        $second = Get-Random -Minimum 0 -Maximum 60
        $timestamp = $date.Date.AddHours($hour).AddMinutes($minute).AddSeconds($second).ToString("yyyy-MM-dd HH:mm:ss")
        
        $user = $users[(Get-Random -Minimum 0 -Maximum $users.Count)]
        $event = $events[(Get-Random -Minimum 0 -Maximum $events.Count)]
        $res = $resources[(Get-Random -Minimum 0 -Maximum $resources.Count)]
        $status = if ((Get-Random -Minimum 0 -Maximum 100) -lt 95) { "SUCCESS" } else { "FAILURE" }
        $duration = Get-Random -Minimum 10 -Maximum 5000
        
        "$timestamp,$user,$event,$res,$status,$duration" | Out-File $out -Append -Encoding utf8
    }
}
