<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { authClient } from '../lib/auth';

const props = defineProps<{
    initialSession?: any
}>();

const isLoggedIn = ref(false);
const isLoading = ref(true);
const displayName = ref('');
const displayInitial = ref('👤');
const userImage = ref<string | null>(null);
const userPhone = ref('');
const preferredChannel = ref('');

const updateState = (user: any) => {
    if (user) {
        isLoggedIn.value = true;
        displayName.value = user.name || user.email || 'User';
        displayInitial.value = (user.name || user.email || '👤').charAt(0).toUpperCase();
        userImage.value = user.imageUrl || user.image || user.picture || null;
        userPhone.value = user.phone || '';
        preferredChannel.value = user.preferredChannel || 'SMS';
    } else {
        isLoggedIn.value = false;
        displayName.value = '';
        displayInitial.value = '👤';
        userImage.value = null;
        userPhone.value = '';
        preferredChannel.value = '';
    }
};

const checkSession = async () => {
    try {
        const { data: sessionData } = await authClient.getSession();
        if (sessionData?.user) {
            // First set from session for quick load
            updateState(sessionData.user);
            
            // Then fetch full Draco profile for phone/channel/image sync
            try {
                const { dracoApiFetch } = await import('../lib/dracoApi');
                const res = await dracoApiFetch('/api/auth/me');
                if (res.ok) {
                    const fullUser = await res.json();
                    updateState(fullUser);
                }
            } catch (e) {
                console.warn("[AuthStatus] Draco profile sync failed:", e);
            }
        } else {
            isLoggedIn.value = false;
        }
    } catch (e) {
        console.error("[AuthStatus] Session check failed:", e);
    }
    isLoading.value = false;
};

const handleSignOut = async () => {
    isLoading.value = true;
    await authClient.signOut();
    window.location.href = '/';
};

onMounted(() => {
    checkSession();
});
</script>

<template>
  <div class="auth-wrapper">
    <template v-if="isLoading">
      <div class="shimmer"></div>
    </template>
    
    <template v-else-if="isLoggedIn">
      <div class="user-profile">
        <a href="/profile" class="profile-link">
          <div class="user-avatar">
            <img v-if="userImage" :src="userImage" class="avatar-img" alt="Profile" />
            <span v-else>{{ displayInitial }}</span>
          </div>
          <div class="user-meta">
            <span class="user-name">{{ displayName }}</span>
          </div>
        </a>
        <button @click="handleSignOut" class="logout-btn" title="Sign Out">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M18.36 6.64a9 9 0 1 1-12.73 0"></path>
            <line x1="12" y1="2" x2="12" y2="12"></line>
          </svg>
        </button>
      </div>
    </template>

    <template v-else>
      <a href="/login" class="signin-btn">
        <span class="btn-text">Sign In</span>
        <div class="btn-glow"></div>
      </a>
    </template>
  </div>
</template>

<style scoped>
.auth-wrapper {
  display: flex;
  align-items: center;
}

.signin-btn {
  position: relative;
  background: var(--dragon-red);
  color: white;
  padding: 0.6rem 1.4rem;
  border-radius: 4px;
  font-weight: 800;
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 1px;
  border: 1px solid #7c0d11;
  overflow: hidden;
  transition: all 0.3s;
  text-decoration: none;
}

.signin-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 5px 15px rgba(177, 17, 22, 0.4);
}

.user-profile {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.25rem;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--border-color);
}

.profile-link {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding-right: 0.5rem;
  text-decoration: none;
}

.user-avatar {
  width: 32px;
  height: 32px;
  background: var(--dragon-red);
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  font-size: 0.85rem;
  font-weight: 900;
  box-shadow: 0 0 10px rgba(177, 17, 22, 0.3);
  overflow: hidden;
}

.avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.user-meta {
  display: flex;
  flex-direction: column;
  line-height: 1;
}

.user-name {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--text-color);
}

.logout-btn {
  background: transparent;
  border: none;
  color: #64748b;
  cursor: pointer;
  padding: 0.5rem;
  display: flex;
  align-items: center;
  border-left: 1px solid var(--border-color);
  transition: all 0.2s;
}

.logout-btn svg {
  width: 16px;
  height: 16px;
}

.logout-btn:hover {
  color: var(--dragon-red);
  background: rgba(177, 17, 22, 0.05);
}

.shimmer {
  width: 120px;
  height: 36px;
  background: linear-gradient(90deg, #1e1e1e 0%, #2a2a2a 50%, #1e1e1e 100%);
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
  border-radius: 4px;
}

@keyframes shimmer {
  0% { background-position: -200% 0; }
  100% { background-position: 200% 0; }
}
</style>
