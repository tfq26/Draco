<template>
  <div class="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50 backdrop-blur-sm">
    <div class="bg-[#1e1e1e] rounded-xl shadow-2xl p-8 max-w-lg w-full border border-gray-700">
      
      <!-- Headers -->
      <div v-if="step === 1" class="text-center">
        <h2 class="text-2xl font-bold text-white mb-2">Initialize Draco Sentinel 🐉</h2>
        <p class="text-gray-400">Let's set up your autonomous cloud governance AI.</p>
      </div>

      <!-- Step 1: User Info & Phone -->
      <div v-if="step === 1" class="mt-6 space-y-4">
        <div>
          <label class="block text-sm font-medium text-gray-300 mb-1">Your Name</label>
          <input 
            v-model="name" 
            type="text" 
            class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            placeholder="e.g. John Doe"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-300 mb-1">Alert Channel</label>
          <select 
            v-model="channel"
            class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option value="WhatsApp">WhatsApp</option>
            <option value="SMS">SMS</option>
          </select>
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-300 mb-1">Phone Number</label>
          <input 
            v-model="phone" 
            type="tel" 
            class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            placeholder="+1 234 567 8900"
          />
        </div>
        <button 
          @click="sendVerification" 
          :disabled="isLoading || !name || !phone"
          class="w-full mt-4 bg-blue-600 hover:bg-blue-700 text-white font-semibold py-3 px-4 rounded-lg transition duration-200 disabled:opacity-50"
        >
          <span v-if="isLoading">Sending Code...</span>
          <span v-else>Send Verification Code</span>
        </button>
      </div>

      <!-- Step 2: Verification Code -->
      <div v-if="step === 2" class="mt-6 space-y-4">
        <h2 class="text-2xl font-bold text-white text-center mb-2">Verify Phone</h2>
        <p class="text-gray-400 text-center text-sm">We sent a 6-digit code via {{ channel }} to {{ phone }}.</p>
        
        <div>
          <label class="block text-sm font-medium text-gray-300 mb-1">Verification Code</label>
          <input 
            v-model="verificationCode" 
            type="text" 
            maxlength="6"
            class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white text-center text-2xl tracking-[0.5em] focus:outline-none focus:ring-2 focus:ring-blue-500"
            placeholder="------"
          />
        </div>
        <button 
          @click="verifyCode" 
          :disabled="isLoading || verificationCode.length !== 6"
          class="w-full mt-4 bg-green-600 hover:bg-green-700 text-white font-semibold py-3 px-4 rounded-lg transition duration-200 disabled:opacity-50"
        >
          <span v-if="isLoading">Verifying...</span>
          <span v-else>Verify & Continue</span>
        </button>
        <p v-if="error" class="text-red-500 text-sm text-center mt-2">{{ error }}</p>
      </div>

      <!-- Step 3: Cloud Credentials -->
      <div v-if="step === 3" class="mt-6 space-y-4">
        <h2 class="text-2xl font-bold text-white text-center mb-2">Connect Cloud</h2>
        <p class="text-gray-400 text-center text-sm">Provide Draco with read-only access to scan your resources.</p>
        
        <div>
          <label class="block text-sm font-medium text-gray-300 mb-1">Cloud Provider</label>
          <select 
            v-model="provider"
            class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option value="Azure">Microsoft Azure</option>
            <option value="AWS">Amazon Web Services</option>
          </select>
        </div>

        <div v-if="provider === 'Azure'" class="space-y-3">
          <input v-model="azure.tenantId" type="text" placeholder="Tenant ID" class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white text-sm" />
          <input v-model="azure.clientId" type="text" placeholder="Client ID (App ID)" class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white text-sm" />
          <input v-model="azure.clientSecret" type="password" placeholder="Client Secret" class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white text-sm" />
          <input v-model="azure.subscriptionId" type="text" placeholder="Subscription ID" class="w-full bg-[#2d2d2d] border border-gray-600 rounded-lg px-4 py-3 text-white text-sm" />
        </div>

        <button 
          @click="finishSetup" 
          :disabled="isLoading"
          class="w-full mt-6 bg-blue-600 hover:bg-blue-700 text-white font-semibold py-3 px-4 rounded-lg transition duration-200 disabled:opacity-50"
        >
          <span v-if="isLoading">Initializing Draco...</span>
          <span v-else>Complete Setup</span>
        </button>
      </div>

      <!-- Step 4: Success -->
       <div v-if="step === 4" class="mt-6 text-center space-y-4">
        <div class="inline-flex items-center justify-center w-16 h-16 rounded-full bg-green-500/20 text-green-500 mb-2">
          <svg class="w-8 h-8" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"></path></svg>
        </div>
        <h2 class="text-2xl font-bold text-white">Setup Complete!</h2>
        <p class="text-gray-400">Draco is now scanning your {{ provider }} environment.<br/>You will receive a {{ channel }} message shortly.</p>
        
        <button 
          @click="closeSetup" 
          class="w-full mt-6 bg-gray-700 hover:bg-gray-600 text-white font-semibold py-3 px-4 rounded-lg transition duration-200"
        >
          Go to Dashboard
        </button>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue';

const emit = defineEmits(['close']);

const step = ref(1);
const isLoading = ref(false);
const error = ref('');

// Step 1
const name = ref('');
const channel = ref('WhatsApp');
const phone = ref('');

// Step 2
const verificationCode = ref('');
const serverCode = ref(''); // Mocking backend

// Step 3
const provider = ref('Azure');
const azure = ref({
  tenantId: '',
  clientId: '',
  clientSecret: '',
  subscriptionId: ''
});

const sendVerification = async () => {
  isLoading.value = true;
  error.value = '';
  
  // MOCK: Hit Draco.Api to trigger Twilio SMS/WhatsApp
  await new Promise(r => setTimeout(r, 1500)); 
  
  serverCode.value = "777888"; // Mock code for demo
  step.value = 2;
  isLoading.value = false;
};

const verifyCode = async () => {
  isLoading.value = true;
  error.value = '';

  await new Promise(r => setTimeout(r, 1000));
  
  // Demo allow anything 6 digits
  step.value = 3;
  isLoading.value = false;
};

const finishSetup = async () => {
  isLoading.value = true;
  
  // MOCK: Sending credentials to Draco.Api
  await new Promise(r => setTimeout(r, 2000));
  
  step.value = 4;
  isLoading.value = false;
};

const closeSetup = () => {
  emit('close');
}
</script>
