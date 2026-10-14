# L'état contient les secrets d'amorçage : il est chiffré par OpenTofu avant d'être écrit (AES-GCM).
terraform {
  encryption {
    key_provider "pbkdf2" "pipeline" {
      passphrase = var.state_passphrase
    }
    method "aes_gcm" "etat" {
      keys = key_provider.pbkdf2.pipeline
    }
    state {
      method   = method.aes_gcm.etat
      enforced = true
    }
    plan {
      method   = method.aes_gcm.etat
      enforced = true
    }
  }
}
