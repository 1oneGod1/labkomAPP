# 📊 Activity Monitoring System - Implementation Plan

## 🎯 Tujuan
Implementasi sistem monitoring aktivitas siswa secara real-time yang mencakup:
1. **Active Window Tracking** - Window/aplikasi yang sedang aktif
2. **Website URL Tracking** - URL yang dibuka di browser
3. **Running Applications** - Daftar aplikasi yang berjalan
4. **Activity Timeline** - Timeline aktivitas dengan timestamp

## 🏗️ Architecture

### Client Side (Electron)
```
┌─────────────────────────────────────┐
│     Activity Monitoring Module       │
├─────────────────────────────────────┤
│ • Active Window Tracker (Windows API)│
│ • Browser URL Detector               │
│ • Running Processes Monitor          │
│ • Activity Logger                    │
└──────────────┬──────────────────────┘
               │
               ▼
┌─────────────────────────────────────┐
│      Realtime Socket / HTTP          │
│  Send activity data to server        │
└─────────────────────────────────────┘
```

### Server Side
```
┌─────────────────────────────────────┐
│   Activity Data Receiver             │
├─────────────────────────────────────┤
│ • REST API Endpoints                 │
│ • Socket.IO Event Handlers           │
│ • Activity Database Storage          │
└──────────────┬──────────────────────┘
               │
               ▼
┌─────────────────────────────────────┐
│       MySQL Database                 │
│  activity_logs table                 │
└─────────────────────────────────────┘
```

### Admin Dashboard
```
┌─────────────────────────────────────┐
│   Activity Monitoring UI             │
├─────────────────────────────────────┤
│ • Live Activity Feed                 │
│ • Activity History View              │
│ • Activity Timeline Graph            │
│ • Filtering & Search                 │
└─────────────────────────────────────┘
```

## 📋 Implementation Steps

### Phase 1: Database Schema
- [ ] Create `activity_logs` table
- [ ] Add indexes for performance
- [ ] Setup data retention policy

### Phase 2: Client - Active Window Tracking
- [ ] Install `active-win` npm package (Windows API wrapper)
- [ ] Create ActivityMonitor class
- [ ] Implement active window polling (every 2-5 seconds)
- [ ] Detect window title & process name

### Phase 3: Client - Browser URL Detection
- [ ] Detect if active window is a browser
- [ ] Extract URL from browser window title
- [ ] Support for Chrome, Edge, Firefox
- [ ] Handle incognito/private mode

### Phase 4: Client - Running Applications
- [ ] Get list of running processes (Windows)
- [ ] Filter system processes
- [ ] Periodic snapshot (every 30 seconds)

### Phase 5: Client - Data Transmission
- [ ] Send activity data via Socket.IO
- [ ] Batch activities for efficiency
- [ ] Handle offline queuing
- [ ] Privacy mode support

### Phase 6: Server - API & Database
- [ ] Create activity endpoints
- [ ] Socket.IO event handlers
- [ ] Store activities in MySQL
- [ ] Implement data aggregation

### Phase 7: Admin Dashboard UI
- [ ] Activity feed component
- [ ] Timeline visualization
- [ ] Search & filter UI
- [ ] Export functionality

### Phase 8: Privacy & Security
- [ ] Configurable monitoring levels
- [ ] Incognito/private detection
- [ ] Data encryption
- [ ] Audit logging

## 🗄️ Database Schema

```sql
CREATE TABLE activity_logs (
  id INT PRIMARY KEY AUTO_INCREMENT,
  pc_name VARCHAR(100) NOT NULL,
  student_id INT,
  student_name VARCHAR(100),
  session_id INT,
  
  -- Activity Type
  activity_type ENUM('window_change', 'browser_url', 'app_list') NOT NULL,
  
  -- Window Activity
  window_title VARCHAR(500),
  process_name VARCHAR(200),
  process_path VARCHAR(500),
  
  -- Browser Activity
  browser_name VARCHAR(50),
  url VARCHAR(2000),
  url_domain VARCHAR(200),
  
  -- Application List (JSON)
  running_apps JSON,
  
  -- Metadata
  is_productive BOOLEAN DEFAULT NULL,
  category VARCHAR(50),
  tags JSON,
  
  -- Timestamps
  activity_at DATETIME NOT NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  
  INDEX idx_pc_activity (pc_name, activity_at),
  INDEX idx_student_activity (student_id, activity_at),
  INDEX idx_session (session_id),
  INDEX idx_activity_type (activity_type),
  INDEX idx_url_domain (url_domain),
  FOREIGN KEY (student_id) REFERENCES students(id) ON DELETE SET NULL,
  FOREIGN KEY (session_id) REFERENCES student_sessions(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

## 📦 Required Dependencies

### Client (Electron)
```json
{
  "dependencies": {
    "active-win": "^8.0.0",
    "node-process-list": "^2.0.0"
  }
}
```

### Server
No additional dependencies needed (use existing MySQL + Socket.IO)

## 🔧 Technical Implementation

### 1. Active Window Monitoring (Client)

```javascript
// client/electron/activityMonitor.js
const activeWin = require('active-win');

class ActivityMonitor {
  constructor() {
    this.currentWindow = null;
    this.monitoringActive = false;
    this.pollInterval = null;
  }

  async start() {
    this.monitoringActive = true;
    this.pollInterval = setInterval(() => {
      this.checkActiveWindow();
    }, 3000); // Check every 3 seconds
  }

  async checkActiveWindow() {
    try {
      const window = await activeWin();
      
      if (this.hasWindowChanged(window)) {
        this.currentWindow = window;
        this.onWindowChange(window);
      }
    } catch (err) {
      // Handle error silently
    }
  }

  hasWindowChanged(newWindow) {
    if (!this.currentWindow) return true;
    return (
      this.currentWindow.title !== newWindow.title ||
      this.currentWindow.owner.name !== newWindow.owner.name
    );
  }

  onWindowChange(window) {
    const activity = {
      type: 'window_change',
      window_title: window.title,
      process_name: window.owner.name,
      process_path: window.owner.path,
      timestamp: new Date().toISOString()
    };

    // Check if browser and extract URL
    if (this.isBrowser(window.owner.name)) {
      const url = this.extractUrlFromTitle(window.title);
      if (url) {
        activity.type = 'browser_url';
        activity.browser_name = window.owner.name;
        activity.url = url;
        activity.url_domain = new URL(url).hostname;
      }
    }

    this.sendActivity(activity);
  }

  isBrowser(processName) {
    const browsers = [
      'chrome', 'msedge', 'firefox', 'brave', 
      'opera', 'vivaldi', 'safari'
    ];
    const lower = processName.toLowerCase();
    return browsers.some(b => lower.includes(b));
  }

  extractUrlFromTitle(title) {
    // Try to extract URL from browser title
    // Format varies: "Page Title - Google Chrome" or "Page Title"
    // URL often in title for some browsers
    
    // Look for URL patterns
    const urlPattern = /https?:\/\/[^\s]+/;
    const match = title.match(urlPattern);
    return match ? match[0] : null;
  }

  sendActivity(activity) {
    // Send to server via realtime socket or HTTP
    // Will be integrated with existing realtime connection
  }

  stop() {
    this.monitoringActive = false;
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
  }
}

module.exports = ActivityMonitor;
```

### 2. Browser URL Detection Enhancement

**Challenge:** Extracting URL from browser window title is unreliable.

**Better Solution:** Use Windows UI Automation API atau browser-specific methods:

```javascript
// For Chrome/Edge: Read from browser accessibility API
// For Firefox: Read from automation API
// Fallback: Window title parsing

// Alternative: Browser extension (requires separate install)
// We'll focus on window title parsing for initial implementation
```

### 3. Running Applications List

```javascript
const { execSync } = require('child_process');

function getRunningApps() {
  try {
    // Windows: Use PowerShell to get processes
    const output = execSync(
      'powershell "Get-Process | Where-Object {$_.MainWindowTitle -ne \\"\\"} | Select-Object Name, MainWindowTitle | ConvertTo-Json"',
      { encoding: 'utf-8', timeout: 5000 }
    );
    
    const processes = JSON.parse(output);
    
    // Filter and clean
    return processes
      .filter(p => p.MainWindowTitle && p.MainWindowTitle.trim())
      .map(p => ({
        name: p.Name,
        window_title: p.MainWindowTitle
      }));
  } catch (err) {
    return [];
  }
}
```

## 🔐 Privacy Considerations

### Privacy Modes:
1. **Full Monitoring** - Track everything (default untuk lab)
2. **Limited Monitoring** - No URL tracking, only app names
3. **Minimal Monitoring** - Only presence detection
4. **Disabled** - No activity tracking

### Sensitive Data Handling:
- Password fields detection → masked as [password entry]
- Private browsing mode → labeled as [private session]
- Certain apps blacklisted → not logged (e.g., password managers)

### Data Retention:
- Default: 30 days
- Configurable per lab policy
- Automatic cleanup job

## 📊 Admin Dashboard Features

### 1. Live Activity Feed
```
┌─────────────────────────────────────────────────────┐
│ 🔴 LIVE ACTIVITY FEED                               │
├─────────────────────────────────────────────────────┤
│ PC-01 | John Doe                     [14:32:15]     │
│ 🌐 Browsing: github.com/user/repo                   │
│                                                      │
│ PC-02 | Jane Smith                   [14:32:10]     │
│ 💻 Using: Visual Studio Code - main.js              │
│                                                      │
│ PC-03 | Bob Wilson                   [14:31:58]     │
│ 🎮 Application: Steam.exe                           │
└─────────────────────────────────────────────────────┘
```

### 2. Activity Timeline
```
14:00 ───●───────●────────●───────────────●─── 15:00
        Code    Browser   Game           Code
```

### 3. Statistics Dashboard
- Most used applications
- Most visited websites
- Productivity score (if categorized)
- Time spent per category

### 4. Alerts & Rules
- Alert if non-productive app detected
- Alert if blacklisted website visited
- Alert if gaming software launched

## 🚀 Deployment Strategy

### Phase 1 - Core Implementation (Week 1)
- Database schema
- Basic active window tracking
- Data transmission to server
- Simple display in admin

### Phase 2 - Enhanced Detection (Week 2)
- Browser URL extraction improvement
- Running apps list
- Activity categorization

### Phase 3 - Dashboard & Analytics (Week 3)
- Rich admin UI
- Timeline visualization
- Search & filter
- Export reports

### Phase 4 - Advanced Features (Week 4)
- Privacy controls
- Alert system
- Productivity analytics
- Performance optimization

## ⚠️ Limitations & Challenges

### Technical Limitations:
1. **Browser URL Detection**: Window title parsing is not 100% reliable
   - Solution: Best effort + fallback to domain extraction
   
2. **Incognito Mode**: Cannot reliably detect
   - Solution: Label as "Private Browsing" based on heuristics

3. **Performance Impact**: Polling every 2-3 seconds
   - Solution: Optimize queries, use efficient native APIs

4. **Cross-Platform**: Currently Windows-only
   - Future: Add macOS/Linux support if needed

### Privacy & Legal:
1. **Student Privacy**: Need clear policy & consent
2. **Data Security**: Encrypt sensitive activity data
3. **Compliance**: Follow local data protection laws

## 📚 References

- `active-win` npm: https://github.com/sindresorhus/active-win
- Windows UI Automation: https://docs.microsoft.com/en-us/windows/win32/winauto
- Browser detection patterns: Common heuristics

---

**Status**: Ready for implementation
**Priority**: High
**Estimated Time**: 2-3 weeks for full implementation
