# Homebrew cask for ShootDrop.
#
#   brew tap nadeem74/shootdrop https://github.com/nadeem74/ShootDrop
#   brew install --cask shootdrop
#
# Always installs the latest GitHub Release for this Mac's chip.
cask "shootdrop" do
  arch arm: "Apple-Silicon", intel: "Intel"

  version :latest
  sha256 :no_check

  url "https://github.com/nadeem74/ShootDrop/releases/latest/download/ShootDrop-macOS-#{arch}.dmg"
  name "ShootDrop"
  desc "Copy camera-card footage into project folders, verified by checksum"
  homepage "https://github.com/nadeem74/ShootDrop"

  depends_on macos: ">= :monterey"

  app "ShootDrop.app"

  # Releases are not notarised by Apple yet (that needs a paid Apple Developer ID).
  # Running this cask is the user's explicit choice to trust it, so clear the
  # download quarantine flag on the app we just installed. Remove this block once
  # releases are notarised.
  postflight do
    system_command "/usr/bin/xattr",
                   args: ["-dr", "com.apple.quarantine", "#{appdir}/ShootDrop.app"]
  end

  uninstall quit:      "io.github.shootdrop.app",
            launchctl: "io.github.shootdrop.app",
            trash:     "~/Library/LaunchAgents/io.github.shootdrop.app.plist"

  zap trash: [
    "~/Library/Application Support/ShootDrop",
    "~/Library/Preferences/io.github.shootdrop.app.plist",
    "~/Library/Saved Application State/io.github.shootdrop.app.savedState",
  ]
end
